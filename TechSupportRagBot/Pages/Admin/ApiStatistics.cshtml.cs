using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using TechSupportRagBot.Data;
using TechSupportRagBot.Models;

namespace TechSupportRagBot.Pages.Admin;

[Authorize(Roles = "Admin")]
public class ApiStatisticsModel : PageModel
{
    private readonly ApplicationDbContext _db;
    public ApiStatisticsModel(ApplicationDbContext db) => _db = db;

    [BindProperty(SupportsGet = true)] public DateTime? DateFrom { get; set; }
    [BindProperty(SupportsGet = true)] public DateTime? DateTo { get; set; }
    public List<UsageRow> Categories { get; private set; } = new();
    public List<UsageRow> Models { get; private set; } = new();
    public decimal TotalCost => Categories.Sum(x => x.CostRub);
    public int TotalRequests => Categories.Sum(x => x.RequestCount);
    public string DonutStyle { get; private set; } = "#e8f1f8 0 100%";

    public async Task OnGetAsync()
    {
        DateTo ??= DateTime.Today;
        DateFrom ??= DateTo.Value.AddDays(-30);
        if (DateFrom > DateTo) (DateFrom, DateTo) = (DateTo, DateFrom);
        var from = DateTime.SpecifyKind(DateFrom.Value.Date, DateTimeKind.Local).ToUniversalTime();
        var to = DateTime.SpecifyKind(DateTo.Value.Date.AddDays(1), DateTimeKind.Local).ToUniversalTime();
        var records = await _db.ApiUsageRecords.AsNoTracking().Where(x => x.CreatedAt >= from && x.CreatedAt < to).ToListAsync();
        Categories = records.GroupBy(x => x.Category).Select(x => new UsageRow(DisplayCategory(x.Key), x.Count(), x.Sum(y => y.InputTokens), x.Sum(y => y.OutputTokens), x.Sum(y => y.EstimatedCostRub))).OrderByDescending(x => x.CostRub).ToList();
        Models = records.GroupBy(x => $"{x.Provider} · {x.Model}").Select(x => new UsageRow(x.Key, x.Count(), x.Sum(y => y.InputTokens), x.Sum(y => y.OutputTokens), x.Sum(y => y.EstimatedCostRub))).OrderByDescending(x => x.CostRub).ToList();
        DonutStyle = BuildDonut(Categories);
    }

    public static string Money(decimal value) => $"{value:N4} ₽";
    private static string DisplayCategory(string value) => value switch
    {
        ApiUsageCategories.BotAnswers => "Ответы бота",
        ApiUsageCategories.KnowledgeFilling => "Заполнение базы",
        ApiUsageCategories.Vectorization => "Векторизация",
        ApiUsageCategories.RagSearch => "RAG-поиск",
        _ => "Прочее"
    };
    private static string BuildDonut(List<UsageRow> rows)
    {
        var colors = new[] { "#168fc8", "#7b61ff", "#18b88f", "#f5a623", "#e65d75" };
        var total = rows.Sum(x => x.CostRub);
        if (total <= 0) return "#e8f1f8 0 100%";
        decimal cursor = 0;
        return string.Join(", ", rows.Select((row, i) => {
            var start = cursor; cursor += row.CostRub / total * 100;
            return $"{colors[i % colors.Length]} {start:F2}% {cursor:F2}%";
        }));
    }
    public sealed record UsageRow(string Name, int RequestCount, int InputTokens, int OutputTokens, decimal CostRub);
}
