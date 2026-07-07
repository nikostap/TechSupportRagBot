namespace TechSupportRagBot.Models;

public class QAEntry
{
    public int Id { get; set; }

    public string Question { get; set; } = string.Empty;

    public string Answer { get; set; } = string.Empty;

    public string? AlternativeQuestions { get; set; }

    public string? Keywords { get; set; }

    public string? MachineModel { get; set; }

    public string? SerialNumber { get; set; }

    public string? NodeName { get; set; }

    public string? Category { get; set; }

    public string? ProblemType { get; set; }

    public string Status { get; set; } = QAEntryStatuses.Draft;

    public string Source { get; set; } = QAEntrySources.Manual;

    public string? CreatedBy { get; set; }

    public ApplicationUser? CreatedByUser { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<KnowledgeChunk> Chunks { get; set; } = new List<KnowledgeChunk>();

    public ICollection<QAAttachment> Attachments { get; set; } = new List<QAAttachment>();
}

public static class QAEntryStatuses
{
    public const string Draft = "Draft";
    public const string Verified = "Verified";
    public const string Deprecated = "Deprecated";
    public const string NeedsReview = "NeedsReview";
}

public static class QAEntrySources
{
    public const string Manual = "Manual";
    public const string Import = "Import";
    public const string Generated = "Generated";
}
