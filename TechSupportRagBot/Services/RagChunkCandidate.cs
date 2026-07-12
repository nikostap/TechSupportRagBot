namespace TechSupportRagBot.Services;

/// <summary>
/// Кандидат чанка с оценками разных этапов поиска.
/// </summary>
public class RagChunkCandidate
{
    public int ChunkId { get; set; }

    public int? DocumentId { get; set; }

    public int? QAEntryId { get; set; }

    public string? QAStatus { get; set; }

    public string? QASource { get; set; }

    public string Text { get; set; } = string.Empty;

    public string? Category { get; set; }

    public int? MachineId { get; set; }

    public string? MachineModel { get; set; }

    public string? SerialNumber { get; set; }

    public string? DocumentName { get; set; }

    public int? Page { get; set; }

    public string? FileName { get; set; }

    public string? SectionTitle { get; set; }

    public string? ErrorName { get; set; }

    public string? ErrorCode { get; set; }

    public string? NodeName { get; set; }

    public string? Tags { get; set; }

    public string? SearchQuestions { get; set; }

    public string? Operations { get; set; }

    public string? DocumentType { get; set; }

    public string? Title { get; set; }

    public string? NormalizedText { get; set; }

    public string? Cause { get; set; }

    public string? Solution { get; set; }

    public string? SubsectionTitle { get; set; }

    public string? SheetName { get; set; }

    public int? RowNumber { get; set; }

    public string? ColumnNames { get; set; }

    public string? ChatDate { get; set; }

    public string? Participants { get; set; }

    public string? Topic { get; set; }

    public string? SourceChat { get; set; }

    public double DenseScore { get; set; }

    public double KeywordScore { get; set; }

    public double RrfScore { get; set; }

    public double RerankScore { get; set; }

    public string? RetrievalReason { get; set; }

    public string SourceType { get; set; } = "Document";
}
