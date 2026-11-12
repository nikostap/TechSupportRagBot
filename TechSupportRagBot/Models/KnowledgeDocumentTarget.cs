namespace TechSupportRagBot.Models;

// One document can apply to a whole machine model or to a particular machine.
public sealed class KnowledgeDocumentTarget
{
    public int Id { get; set; }
    public int KnowledgeDocumentId { get; set; }
    public KnowledgeDocument KnowledgeDocument { get; set; } = null!;
    public string? MachineModel { get; set; }
    public int? MachineId { get; set; }
    public Machine? Machine { get; set; }
}
