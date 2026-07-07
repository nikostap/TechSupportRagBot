namespace TechSupportRagBot.Models;

public class OperatorChatPresence
{
    public int Id { get; set; }

    public string OperatorUserId { get; set; } = string.Empty;

    public ApplicationUser? OperatorUser { get; set; }

    public int TicketId { get; set; }

    public Ticket? Ticket { get; set; }

    public DateTime LastActivityAt { get; set; } = DateTime.UtcNow;
}
