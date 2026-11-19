namespace TechSupportRagBot.Models;

public sealed class BotTestSession
{
    public string UserId { get; set; } = string.Empty;
    public int MachineId { get; set; }
    public string Language { get; set; } = "Russian";
    public string HistoryJson { get; set; } = string.Empty;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
