namespace TechSupportRagBot.Models;

public class TicketOperatorAssignment
{
    public int Id { get; set; }

    public int TicketId { get; set; }

    public Ticket? Ticket { get; set; }

    public string OperatorUserId { get; set; } = string.Empty;

    public ApplicationUser? OperatorUser { get; set; }

    public DateTime AssignedAt { get; set; } = DateTime.UtcNow;
}
