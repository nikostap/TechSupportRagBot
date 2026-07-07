namespace TechSupportRagBot.Models;

/// <summary>
/// Решенный вопрос, который позже можно добавить в RAG как источник знаний.
/// </summary>
public class ResolvedAnswer
{
    public int Id { get; set; }

    public int TicketId { get; set; }

    public Ticket? Ticket { get; set; }

    public int MachineId { get; set; }

    public Machine? Machine { get; set; }

    public string Question { get; set; } = string.Empty;

    public string Answer { get; set; } = string.Empty;

    public string Category { get; set; } = "Решённые обращения";

    public string? QdrantPointId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
