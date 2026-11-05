namespace TechSupportRagBot.Models;

public class MonthlyPasswordEmailLog
{
    public int Id { get; set; }
    public int ClientId { get; set; }
    public Client? Client { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    public string RecipientEmail { get; set; } = string.Empty;
    public DateTime SentAt { get; set; } = DateTime.UtcNow;
}
