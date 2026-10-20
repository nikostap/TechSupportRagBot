using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TechSupportRagBot.Data;
using TechSupportRagBot.Models;
using WebPush;

namespace TechSupportRagBot.Services;

public sealed class BrowserPushService(
    ApplicationDbContext db, IConfiguration configuration, ILogger<BrowserPushService> logger)
{
    private static readonly HttpClient PushHttpClient = new() { Timeout = TimeSpan.FromSeconds(12) };
    public string? PublicKey => configuration["Push:PublicKey"];
    public bool IsConfigured => !string.IsNullOrWhiteSpace(PublicKey)
        && !string.IsNullOrWhiteSpace(configuration["Push:PrivateKey"]);

    public static bool IsAllowedEndpoint(string? value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps || uri.Port != 443
            || !string.IsNullOrEmpty(uri.UserInfo) || uri.HostNameType != UriHostNameType.Dns)
            return false;

        var host = uri.Host.ToLowerInvariant();
        return host is "fcm.googleapis.com" or "android.googleapis.com"
            or "updates.push.services.mozilla.com"
            || host.EndsWith(".push.apple.com", StringComparison.Ordinal)
            || host.EndsWith(".notify.windows.com", StringComparison.Ordinal);
    }

    public async Task<bool> SubscribeAsync(string userId, string endpoint, string p256dh, string auth, CancellationToken ct)
    {
        if (!IsConfigured || !IsAllowedEndpoint(endpoint) || endpoint.Length > 2048
            || !ValidKey(p256dh, 40, 200) || !ValidKey(auth, 12, 100)) return false;

        var record = await db.BrowserPushSubscriptions.FirstOrDefaultAsync(x => x.Endpoint == endpoint, ct);
        if (record == null)
        {
            record = new BrowserPushSubscription { Endpoint = endpoint };
            db.BrowserPushSubscriptions.Add(record);
        }
        record.UserId = userId;
        record.P256dh = p256dh;
        record.Auth = auth;
        record.LastSeenAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task UnsubscribeAsync(string userId, string endpoint, CancellationToken ct)
    {
        await db.BrowserPushSubscriptions
            .Where(x => x.UserId == userId && x.Endpoint == endpoint)
            .ExecuteDeleteAsync(ct);
    }

    public async Task SendAsync(string type, int ticketId, int? messageId, ApplicationUser recipient, CancellationToken ct)
    {
        if (!IsConfigured) return;
        var eventKey = $"{type}:{ticketId}:{messageId?.ToString() ?? "-"}";
        var subscriptions = await db.BrowserPushSubscriptions
            .Where(x => x.UserId == recipient.Id)
            .ToListAsync(ct);
        var details = new VapidDetails("mailto:support@classeng.com", PublicKey!, configuration["Push:PrivateKey"]!);
        var titleKey = type switch
        {
            EmailNotificationTypes.WaitingForOperator => "PushWaitingTitle",
            EmailNotificationTypes.OperatorAssigned => "PushAssignedTitle",
            EmailNotificationTypes.UnreadMessage => "PushUnreadTitle",
            _ => "PushNewMessageTitle"
        };
        var payload = JsonSerializer.Serialize(new
        {
            titleRu = UiText.ForLanguage("ru", titleKey),
            titleEn = UiText.ForLanguage("en", titleKey),
            bodyRu = string.Format(UiText.ForLanguage("ru", "PushTicketBody"), ticketId),
            bodyEn = string.Format(UiText.ForLanguage("en", "PushTicketBody"), ticketId),
            url = $"/Operator/Ticket/{ticketId}",
            tag = eventKey
        });
        foreach (var subscription in subscriptions)
        {
            if (await db.BrowserPushLogs.AnyAsync(x => x.SubscriptionId == subscription.Id && x.EventKey == eventKey, ct))
                continue;
            try
            {
                var client = new WebPushClient(PushHttpClient);
                await client.SendNotificationAsync(
                    new PushSubscription(subscription.Endpoint, subscription.P256dh, subscription.Auth),
                    payload, details, ct);
                db.BrowserPushLogs.Add(new BrowserPushLog { SubscriptionId = subscription.Id, EventKey = eventKey });
                await db.SaveChangesAsync(ct);
            }
            catch (WebPushException ex) when (ex.StatusCode is HttpStatusCode.Gone or HttpStatusCode.NotFound)
            {
                await db.BrowserPushSubscriptions.Where(x => x.Id == subscription.Id).ExecuteDeleteAsync(ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Browser push delivery failed for event {EventKey}, subscription {Id}", eventKey, subscription.Id);
            }
        }
    }

    private static bool ValidKey(string value, int min, int max) =>
        value.Length >= min && value.Length <= max && value.All(c => char.IsLetterOrDigit(c) || c is '-' or '_');
}
