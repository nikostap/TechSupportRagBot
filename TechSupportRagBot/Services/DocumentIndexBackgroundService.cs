using Microsoft.EntityFrameworkCore;
using TechSupportRagBot.Data;
using TechSupportRagBot.Models;

namespace TechSupportRagBot.Services;

public sealed class DocumentIndexBackgroundService(
    IServiceScopeFactory scopeFactory,
    ILogger<DocumentIndexBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RecoverAsync(stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            var currentId = 0;
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var id = await db.KnowledgeDocuments.AsNoTracking()
                    .Where(x => x.DocumentKind != "Other" && x.IndexStatus == DocumentIndexStates.Queued)
                    .OrderBy(x => x.IndexUpdatedAt).ThenBy(x => x.Id)
                    .Select(x => x.Id).FirstOrDefaultAsync(stoppingToken);
                if (id == 0)
                {
                    await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
                    continue;
                }

                var claimed = await db.KnowledgeDocuments
                    .Where(x => x.Id == id && x.IndexStatus == DocumentIndexStates.Queued && !x.ClientDownloadAllowed)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(x => x.IndexStatus, DocumentIndexStates.Processing)
                        .SetProperty(x => x.IndexUpdatedAt, DateTime.UtcNow), stoppingToken);
                if (claimed == 0) continue;
                currentId = id;

                var document = await db.KnowledgeDocuments.FindAsync([id], stoppingToken);
                if (document == null) continue;
                await scope.ServiceProvider.GetRequiredService<KnowledgeIngestionService>()
                    .IndexDocumentAsync(document, stoppingToken, componentJob: true);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Document indexing worker failed for document {DocumentId}.", currentId);
                if (currentId != 0)
                {
                    try
                    {
                        await using var failureScope = scopeFactory.CreateAsyncScope();
                        var failureDb = failureScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                        await failureDb.KnowledgeDocuments
                            .Where(x => x.Id == currentId && x.IndexStatus == DocumentIndexStates.Processing)
                            .ExecuteUpdateAsync(setters => setters
                                .SetProperty(x => x.IndexStatus, DocumentIndexStates.Failed)
                                .SetProperty(x => x.IndexCompletedChunks, 0)
                                .SetProperty(x => x.IndexErrorCode, "DocumentIndexFailed")
                                .SetProperty(x => x.IndexUpdatedAt, DateTime.UtcNow), stoppingToken);
                    }
                    catch (Exception updateError) when (!stoppingToken.IsCancellationRequested)
                    {
                        logger.LogError(updateError, "Could not save indexing failure for document {DocumentId}.", currentId);
                    }
                }
                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
            }
        }
    }

    private async Task RecoverAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.KnowledgeDocuments
            .Where(x => x.DocumentKind != "Other" && x.IndexStatus == DocumentIndexStates.Processing)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.IndexStatus, DocumentIndexStates.Queued)
                .SetProperty(x => x.IndexUpdatedAt, DateTime.UtcNow), cancellationToken);
    }
}
