namespace TechSupportRagBot.Models;

public class ChatMessageTranslation
{
    public int Id { get; set; }

    public int ChatMessageId { get; set; }

    public ChatMessage? ChatMessage { get; set; }

    public string TargetLanguage { get; set; } = string.Empty;

    public string SourceText { get; set; } = string.Empty;

    public string Text { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
