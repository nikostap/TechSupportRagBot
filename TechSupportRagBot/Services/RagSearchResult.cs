namespace TechSupportRagBot.Services;

/// <summary>
/// Итог гибридного RAG-поиска после dense search, FTS5, RRF и rerank.
/// </summary>
public class RagSearchResult
{
    public string? TraceId { get; set; }

    public IReadOnlyList<RagChunkCandidate> Chunks { get; set; } = Array.Empty<RagChunkCandidate>();

    public bool ShouldCallOperator { get; set; }

    public double Confidence { get; set; }

    public string? Warning { get; set; }

    public RagSearchDebugInfo Debug { get; set; } = new();
}

public class RagSearchDebugInfo
{
    public string Query { get; set; } = string.Empty;

    public string NormalizedQuery { get; set; } = string.Empty;

    public string QueryIntent { get; set; } = "GeneralKnowledge";

    public IReadOnlyList<RagSearchDebugHit> VectorHits { get; set; } = Array.Empty<RagSearchDebugHit>();

    public IReadOnlyList<RagSearchDebugHit> KeywordHits { get; set; } = Array.Empty<RagSearchDebugHit>();

    public IReadOnlyList<RagSearchDebugHit> MergedHits { get; set; } = Array.Empty<RagSearchDebugHit>();

    public IReadOnlyList<RagSearchDebugHit> RerankedHits { get; set; } = Array.Empty<RagSearchDebugHit>();
}

public class RagSearchDebugHit
{
    public int ChunkId { get; set; }

    public int Rank { get; set; }

    public double DenseScore { get; set; }

    public double KeywordScore { get; set; }

    public double RrfScore { get; set; }

    public double RerankScore { get; set; }

    public string? RetrievalReason { get; set; }

    public int? QAEntryId { get; set; }

    public string? QAStatus { get; set; }

    public string? DocumentType { get; set; }
}
