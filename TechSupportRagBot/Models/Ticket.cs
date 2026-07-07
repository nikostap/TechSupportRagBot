namespace TechSupportRagBot.Models;

/// <summary>
/// Обращение клиента по конкретному станку.
/// </summary>
public class Ticket
{
    public int Id { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Status { get; set; } = TicketStatuses.New;

    public string ClientUserId { get; set; } = string.Empty;

    public ApplicationUser? ClientUser { get; set; }

    public string? OperatorUserId { get; set; }

    public ApplicationUser? OperatorUser { get; set; }

    public int MachineId { get; set; }

    public Machine? Machine { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? ClosedAt { get; set; }

    public ICollection<ChatMessage> Messages { get; set; } = new List<ChatMessage>();

    public ICollection<TicketOperatorAssignment> OperatorAssignments { get; set; } = new List<TicketOperatorAssignment>();

    public ICollection<ResolvedAnswer> ResolvedAnswers { get; set; } = new List<ResolvedAnswer>();
}

public static class TicketStatuses
{
    public const string New = "New";
    public const string BotAnswered = "BotAnswered";
    public const string WaitingForOperator = "WaitingForOperator";
    public const string InProgress = "InProgress";
    public const string Closed = "Closed";
}
