using TechSupportRagBot.Data;
using TechSupportRagBot.Models;

namespace TechSupportRagBot.Services;

public sealed class ApiUsageService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ApiUsageService> _logger;

    public ApiUsageService(IServiceScopeFactory scopeFactory, ILogger<ApiUsageService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task RecordAsync(string provider, string model, string category, string operation,
        int inputTokens, int outputTokens, decimal? reportedCostRub, CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.ApiUsageRecords.Add(new ApiUsageRecord
            {
                Provider = provider,
                Model = model,
                Category = category,
                Operation = operation,
                InputTokens = Math.Max(0, inputTokens),
                OutputTokens = Math.Max(0, outputTokens),
                EstimatedCostRub = Estimate(model, inputTokens, outputTokens) is var estimate && estimate > 0
                    ? estimate
                    : reportedCostRub ?? 0
            });
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist API usage statistics.");
        }
    }

    private static decimal Estimate(string model, int inputTokens, int outputTokens)
    {
        var normalized = model.ToLowerInvariant();
        var (inputRate, outputRate) = normalized switch
        {
            var x when x.Contains("text-embedding-v4") => (14.4m, 0m),
            var x when x.Contains("deepseek-chat") => (28m, 56m),
            _ => (0m, 0m)
        };
        return Math.Round(inputTokens * inputRate / 1_000_000m + outputTokens * outputRate / 1_000_000m, 6);
    }
}
