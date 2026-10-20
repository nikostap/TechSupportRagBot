using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TechSupportRagBot.Data;
using TechSupportRagBot.Models;

namespace TechSupportRagBot.Services;

public sealed class EmailNotificationRulesService(ApplicationDbContext db)
{
    public static readonly (string Key, string NameKey)[] InternalProfiles =
    [
        (AccessProfileService.Administrator, "ProfileAdministrator"),
        (AccessProfileService.Manager, "ProfileManager"),
        (AccessProfileService.Engineer, "ProfileEngineer"),
        (AccessProfileService.Operator, "ProfileOperator"),
        (AccessProfileService.Programmer, "ProfileProgrammer"),
        (AccessProfileService.Observer, "ProfileObserver")
    ];

    public static readonly (string Key, string NameKey)[] EditableEvents =
    [
        (EmailNotificationTypes.WaitingForOperator, "MailEventWaiting"),
        (EmailNotificationTypes.OperatorAssigned, "MailEventAssigned"),
        (EmailNotificationTypes.NewMessage, "MailEventNewMessage"),
        (EmailNotificationTypes.UnreadMessage, "MailEventUnread")
    ];

    public static readonly string[] PersonalEventNameKeys =
    [
        "MailEventWelcome", "MailEventRecovery", "MailEventLicenseKey",
        "MailEventLicenseSevenDays", "MailEventLicenseOneDay"
    ];

    public async Task<Dictionary<string, List<string>>> GetAsync(CancellationToken cancellationToken = default)
    {
        var raw = await db.SystemSettings.AsNoTracking()
            .Where(x => x.Key == SystemSettingKeys.EmailNotificationRules)
            .Select(x => x.Value)
            .FirstOrDefaultAsync(cancellationToken);
        Dictionary<string, List<string>>? saved = null;
        if (!string.IsNullOrWhiteSpace(raw))
        {
            try { saved = JsonSerializer.Deserialize<Dictionary<string, List<string>>>(raw); }
            catch (JsonException) { /* Fall back to defaults. */ }
        }

        var allProfiles = InternalProfiles.Select(x => x.Key).ToArray();
        return EditableEvents.ToDictionary(x => x.Key, x =>
            (saved != null && saved.TryGetValue(x.Key, out var profiles)
                ? profiles ?? []
                : x.Key == EmailNotificationTypes.WaitingForOperator
                    ? [AccessProfileService.Manager]
                    : allProfiles.ToList())
            .Where(profile => allProfiles.Contains(profile, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList());
    }

    public async Task SaveAsync(Dictionary<string, List<string>> rules, CancellationToken cancellationToken = default)
    {
        var allProfiles = InternalProfiles.Select(x => x.Key).ToArray();
        var cleaned = EditableEvents.ToDictionary(x => x.Key, x =>
            (rules.TryGetValue(x.Key, out var profiles) ? profiles : [])
            .Where(profile => allProfiles.Contains(profile, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList());
        var setting = await db.SystemSettings.FirstOrDefaultAsync(x => x.Key == SystemSettingKeys.EmailNotificationRules, cancellationToken);
        if (setting == null)
        {
            db.SystemSettings.Add(new SystemSetting { Key = SystemSettingKeys.EmailNotificationRules,
                Value = JsonSerializer.Serialize(cleaned) });
        }
        else
        {
            setting.Value = JsonSerializer.Serialize(cleaned);
            setting.UpdatedAt = DateTime.UtcNow;
        }
        await db.SaveChangesAsync(cancellationToken);
    }

    public static bool Allows(IReadOnlyDictionary<string, List<string>> rules, string eventKey, ApplicationUser user) =>
        user.ClientId == null && !string.IsNullOrWhiteSpace(user.AccessProfile)
        && rules.TryGetValue(eventKey, out var profiles)
        && profiles.Contains(user.AccessProfile, StringComparer.OrdinalIgnoreCase);
}
