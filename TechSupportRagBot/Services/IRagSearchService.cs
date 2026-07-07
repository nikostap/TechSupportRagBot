namespace TechSupportRagBot.Services;

public interface IRagSearchService
{
    Task<RagSearchResult> SearchAsync(RagSearchRequest request, CancellationToken cancellationToken = default);

    string BuildContextForLlm(RagSearchResult result);
}
