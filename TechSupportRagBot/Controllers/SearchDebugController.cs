using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TechSupportRagBot.Services;

namespace TechSupportRagBot.Controllers;

[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/search")]
public class SearchDebugController : ControllerBase
{
    private readonly IRagSearchService _search;
    private readonly KnowledgeIngestionService _ingestion;

    public SearchDebugController(IRagSearchService search, KnowledgeIngestionService ingestion)
    {
        _search = search;
        _ingestion = ingestion;
    }

    [HttpGet("test")]
    public async Task<IActionResult> Test(
        [FromQuery] string? query,
        [FromQuery] string? question,
        [FromQuery] int? machineId,
        [FromQuery] int topK = 8,
        CancellationToken cancellationToken = default)
    {
        var searchQuery = string.IsNullOrWhiteSpace(query) ? question : query;
        if (string.IsNullOrWhiteSpace(searchQuery))
        {
            return BadRequest(new { error = "Query parameter 'query' is required." });
        }

        var traceId = Guid.NewGuid().ToString("N");
        var result = await _search.SearchAsync(new RagSearchRequest
        {
            TraceId = traceId,
            Question = searchQuery,
            MachineId = machineId,
            DenseTopK = 50,
            KeywordTopK = 50,
            FinalTopK = Math.Clamp(topK, 5, 8)
        }, cancellationToken);

        return Ok(new
        {
            traceId,
            query = searchQuery,
            normalizedQuery = result.Debug.NormalizedQuery,
            queryIntent = result.Debug.QueryIntent,
            machineId,
            result.Confidence,
            result.ShouldCallOperator,
            result.Warning,
            qaFound = result.Chunks.Any(x => x.DocumentType == "QA"),
            qa = result.Chunks
                .Where(x => x.DocumentType == "QA")
                .Select(x => new
                {
                    x.QAEntryId,
                    x.QAStatus,
                    x.QASource,
                    x.RerankScore,
                    x.RetrievalReason,
                    reason = x.QAStatus == "Verified" ? "Verified QA can be used as primary answer source." : "QA was found but is not verified."
                }),
            vectorSearch = result.Debug.VectorHits,
            bm25Search = result.Debug.KeywordHits,
            merged = result.Debug.MergedHits,
            reranked = result.Debug.RerankedHits,
            chunks = result.Chunks.Select((chunk, index) => new
            {
                rank = index + 1,
                chunk.ChunkId,
                chunk.DocumentId,
                chunk.QAEntryId,
                chunk.QAStatus,
                chunk.QASource,
                chunk.DocumentType,
                chunk.Title,
                chunk.NormalizedText,
                chunk.DocumentName,
                chunk.MachineId,
                chunk.MachineModel,
                chunk.SerialNumber,
                chunk.Category,
                chunk.FileName,
                chunk.Page,
                chunk.SheetName,
                chunk.RowNumber,
                chunk.ColumnNames,
                chunk.SectionTitle,
                chunk.SubsectionTitle,
                chunk.ErrorCode,
                chunk.ErrorName,
                chunk.Cause,
                chunk.Solution,
                chunk.NodeName,
                chunk.Participants,
                chunk.Topic,
                chunk.SourceChat,
                scores = new
                {
                    chunk.DenseScore,
                    chunk.KeywordScore,
                    chunk.RrfScore,
                    chunk.RerankScore
                },
                chunk.RetrievalReason,
                chunk.SourceType,
                chunk.Text
            })
        });
    }

    [HttpPost("reindex")]
    public async Task<IActionResult> Reindex(CancellationToken cancellationToken = default)
    {
        await _ingestion.ReindexAllDocumentsAsync(cancellationToken);
        return Ok(new { ok = true });
    }
}
