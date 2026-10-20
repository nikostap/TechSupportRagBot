namespace TechSupportRagBot.Models;

public class LicenseEmailLog
{
    public int Id { get; set; }
    public int LicenseId { get; set; }
    public License? License { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string RecipientEmail { get; set; } = string.Empty;
    public DateTime SentAt { get; set; } = DateTime.UtcNow;
}

public static class LicenseEmailKinds
{
    public const string SevenDays = "SevenDays";
    public const string OneDay = "OneDay";
}
