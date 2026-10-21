namespace TechSupportRagBot.Models;

public sealed class ChatMessageOperatorRead
{
    public int ChatMessageId { get; set; }
    public ChatMessage ChatMessage { get; set; } = null!;
    public string OperatorUserId { get; set; } = string.Empty;
    public ApplicationUser OperatorUser { get; set; } = null!;
    public DateTime ReadAt { get; set; } = DateTime.UtcNow;
}
