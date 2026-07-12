namespace TechSupportRagBot.Models;

public class OperatorChatTimeEntry
{
    public int Id { get; set; }

    public string? OperatorUserId { get; set; }

    public ApplicationUser? OperatorUser { get; set; }

    public int? TicketId { get; set; }

    public Ticket? Ticket { get; set; }

    public int? MachineId { get; set; }

    public Machine? Machine { get; set; }

    public string OperatorName { get; set; } = string.Empty;

    public string MachineModel { get; set; } = string.Empty;

    public string TicketReference { get; set; } = string.Empty;

    public DateTime StartedAt { get; set; }

    public DateTime EndedAt { get; set; }

    public int WorkSeconds { get; set; }

    public int OvertimeSeconds { get; set; }

    public int TotalSeconds => WorkSeconds + OvertimeSeconds;
}
