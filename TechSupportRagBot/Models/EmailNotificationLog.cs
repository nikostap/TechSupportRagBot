namespace TechSupportRagBot.Models;

public class EmailNotificationLog
{
    public int Id { get; set; }

    public string NotificationType { get; set; } = string.Empty;

    public int? TicketId { get; set; }

    public Ticket? Ticket { get; set; }

    public int? ChatMessageId { get; set; }

    public ChatMessage? ChatMessage { get; set; }

    public string? RecipientUserId { get; set; }

    public ApplicationUser? RecipientUser { get; set; }

    public string RecipientEmail { get; set; } = string.Empty;

    public DateTime SentAt { get; set; } = DateTime.UtcNow;
}

public static class EmailNotificationTypes
{
    public const string OperatorAssigned = "OperatorAssigned";
    public const string WaitingForOperator = "WaitingForOperator";
    public const string UnreadMessage = "UnreadMessage";
}
