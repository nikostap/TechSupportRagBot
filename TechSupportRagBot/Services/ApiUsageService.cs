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
                // AITUNNEL возвращает фактическое списание в usage.cost_rub. Оно важнее
                // расчёта по тарифу: у провайдера есть минимальная стоимость запроса.
                EstimatedCostRub = CalculateCost(model, operation, inputTokens, outputTokens, reportedCostRub)
            });
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist API usage statistics.");
        }
    }

    private static decimal CalculateCost(string model, string operation, int inputTokens, int outputTokens, decimal? reportedCostRub)
    {
        var cost = reportedCostRub is > 0
            ? reportedCostRub.Value
            : Estimate(model, inputTokens, outputTokens);

        // В API AITUNNEL для embeddings действует минимальное списание 0,01 ₽ за запрос.
        if (string.Equals(operation, "Embedding", StringComparison.OrdinalIgnoreCase))
        {
            cost = Math.Max(0.01m, cost);
        }

        return Math.Round(cost, 6);
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
