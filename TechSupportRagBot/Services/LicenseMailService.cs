using System.Net;
using Microsoft.EntityFrameworkCore;
using TechSupportRagBot.Data;
using TechSupportRagBot.Models;

namespace TechSupportRagBot.Services;

public enum LicenseKeyMailResult { Sent, NoEmail, Unavailable }

public sealed class LicenseMailService(
    ApplicationDbContext db,
    IEmailSender sender,
    IConfiguration configuration,
    ILogger<LicenseMailService> logger)
{
    public async Task<LicenseKeyMailResult> SendKeyAsync(int licenseId, CancellationToken cancellationToken = default)
    {
        var license = await db.Licenses.AsNoTracking()
            .Include(x => x.Client).ThenInclude(x => x!.Users).Include(x => x.Machine)
            .FirstOrDefaultAsync(x => x.Id == licenseId, cancellationToken);
        if (license == null || !license.IsActive || license.ExpiresAt < DateTime.UtcNow)
            return LicenseKeyMailResult.Unavailable;
        var email = license.Client == null ? null : RecipientEmail(license.Client);
        if (license.Client == null || email == null)
            return LicenseKeyMailResult.NoEmail;

        var body = string.Format(UiText.ForLanguage("ru", "LicenseKeyMailBody"),
            WebUtility.HtmlEncode(license.Client.Name),
            WebUtility.HtmlEncode(license.Machine?.Name),
            WebUtility.HtmlEncode(license.Machine?.Model),
            WebUtility.HtmlEncode(license.Key),
            license.ExpiresAt?.ToString("dd.MM.yyyy") ?? UiText.ForLanguage("ru", "NoExpiry"),
            "https://support.classeng.com/Client/Activate");
        await sender.SendAsync(email,
            UiText.ForLanguage("ru", "LicenseKeyMailSubject"), body, cancellationToken);
        return LicenseKeyMailResult.Sent;
    }

    public async Task SendExpiryRemindersAsync(CancellationToken cancellationToken = default)
    {
        if (!configuration.GetValue("Notifications:LicenseReminderEnabled", false)) return;

        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow,
            TimeZoneInfo.FindSystemTimeZoneById("Europe/Moscow")));
        var firstExpiry = DateTime.SpecifyKind(today.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
        var lastExpiry = firstExpiry.AddDays(8);
        var licenses = await db.Licenses.AsNoTracking()
            .Include(x => x.Client).ThenInclude(x => x!.Users).Include(x => x.Machine)
            .Where(x => x.IsActive && x.ExpiresAt >= firstExpiry && x.ExpiresAt < lastExpiry)
            .ToListAsync(cancellationToken);

        foreach (var license in licenses)
        {
            var daysLeft = DateOnly.FromDateTime(license.ExpiresAt!.Value).DayNumber - today.DayNumber;
            var kind = daysLeft switch
            {
                >= 2 and <= 7 => LicenseEmailKinds.SevenDays,
                1 => LicenseEmailKinds.OneDay,
                _ => null
            };
            if (kind == null || license.Client == null) continue;

            var email = RecipientEmail(license.Client);
            if (email == null) continue;
            if (await db.LicenseEmailLogs.AnyAsync(x => x.LicenseId == license.Id
                && x.Kind == kind && x.RecipientEmail == email, cancellationToken)) continue;

            var body = string.Format(UiText.ForLanguage("ru", "LicenseExpiryMailBody"),
                WebUtility.HtmlEncode(license.Client.Name),
                WebUtility.HtmlEncode(license.Machine?.Name),
                license.ExpiresAt.Value.ToString("dd.MM.yyyy"),
                daysLeft,
                "https://support.classeng.com/Client/NewTicket");
            try
            {
                await sender.SendAsync(email, UiText.ForLanguage("ru", "LicenseExpiryMailSubject"),
                    body, cancellationToken);
                db.LicenseEmailLogs.Add(new LicenseEmailLog
                {
                    LicenseId = license.Id, Kind = kind, RecipientEmail = email
                });
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not send license expiry reminder for license {LicenseId}.", license.Id);
            }
        }
    }

    private static string? RecipientEmail(Client client) =>
        !string.IsNullOrWhiteSpace(client.ContactEmail) ? client.ContactEmail.Trim() :
        client.Users.OrderBy(x => x.CreatedAt).Select(x => x.Email)
            .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))?.Trim();
}
