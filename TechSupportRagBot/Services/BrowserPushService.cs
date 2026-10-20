using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
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
        if (subscriptions.Count == 0) return;
        var ticket = await db.Tickets.AsNoTracking()
            .Include(x => x.Machine)
            .Include(x => x.ClientUser).ThenInclude(x => x!.Client)
            .FirstOrDefaultAsync(x => x.Id == ticketId, ct);
        if (ticket == null) return;
        var message = messageId == null ? null : await db.ChatMessages.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == messageId && x.TicketId == ticketId, ct);
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
            titleRu = BuildTitle("ru", titleKey, ticket),
            titleEn = BuildTitle("en", titleKey, ticket),
            bodyRu = BuildBody("ru", ticket, message),
            bodyEn = BuildBody("en", ticket, message),
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

    private static string BuildTitle(string language, string titleKey, Ticket ticket)
    {
        var subject = Shorten(ticket.Title, 48);
        var client = Shorten(ticket.ClientUser?.Client?.Name ?? ticket.ClientUser?.FullName
            ?? ticket.ClientUser?.UserName, 32);
        return string.Format(UiText.ForLanguage(language, "PushContextTitle"),
            UiText.ForLanguage(language, titleKey), subject, client);
    }

    private static string BuildBody(string language, Ticket ticket, ChatMessage? message)
    {
        var machine = ticket.Machine;
        var machineName = Shorten(machine?.Name, 24);
        var model = Shorten(machine?.Model, 28);
        var serial = Shorten(machine?.SerialNumber, 20);
        var equipment = string.Join(" ", new[] { machineName, model, serial }.Where(x => x.Length > 0));
        var preview = message == null ? string.Empty : string.IsNullOrWhiteSpace(message.Text)
            ? UiText.ForLanguage(language, "PushAttachmentPreview") : Shorten(message.Text, 100);
        return preview.Length == 0 ? equipment : string.Format(UiText.ForLanguage(language, "PushContextBody"),
            equipment, preview);
    }

    private static string Shorten(string? value, int maxLength)
    {
        var normalized = Regex.Replace(value ?? string.Empty, @"\s+", " ").Trim();
        return normalized.Length <= maxLength ? normalized : normalized[..(maxLength - 1)].TrimEnd() + "…";
    }

    private static bool ValidKey(string value, int min, int max) =>
        value.Length >= min && value.Length <= max && value.All(c => char.IsLetterOrDigit(c) || c is '-' or '_');
}
