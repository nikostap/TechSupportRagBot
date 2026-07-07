namespace TechSupportRagBot.Models;

/// <summary>
/// Сообщение внутри обращения.
/// </summary>
public class ChatMessage
{
    public int Id { get; set; }

    public int TicketId { get; set; }

    public Ticket? Ticket { get; set; }

    public string AuthorUserId { get; set; } = string.Empty;

    public ApplicationUser? AuthorUser { get; set; }

    public string Text { get; set; } = string.Empty;

    public bool IsBotMessage { get; set; }

    public bool IsReadByClient { get; set; }

    public bool IsReadByOperator { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<Attachment> Attachments { get; set; } = new List<Attachment>();

    public ICollection<ChatMessageTranslation> Translations { get; set; } = new List<ChatMessageTranslation>();
}
