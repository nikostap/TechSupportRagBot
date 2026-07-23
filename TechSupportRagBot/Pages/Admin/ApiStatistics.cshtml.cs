using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using TechSupportRagBot.Data;
using TechSupportRagBot.Models;

namespace TechSupportRagBot.Pages.Admin;

[Authorize(Roles = "Admin")]
public class ApiStatisticsModel : PageModel
{
    private static readonly TimeZoneInfo MoscowTimeZone = ResolveMoscowTimeZone();
    private static readonly string[] DonutColors = ["#168fc8", "#7b61ff", "#18b88f", "#f5a623", "#e65d75"];
    private readonly ApplicationDbContext _db;
    public ApiStatisticsModel(ApplicationDbContext db) => _db = db;

    [BindProperty(SupportsGet = true)] public DateOnly? DateFrom { get; set; }
    [BindProperty(SupportsGet = true)] public DateOnly? DateTo { get; set; }
    public List<UsageRow> Categories { get; private set; } = new();
    public List<UsageRow> Models { get; private set; } = new();
    public decimal TotalCost => Categories.Sum(x => x.CostRub);
    public int TotalRequests => Categories.Sum(x => x.RequestCount);
    public string DonutStyle { get; private set; } = "#e8f1f8 0 100%";

    public async Task OnGetAsync()
    {
        DateTo ??= DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, MoscowTimeZone));
        DateFrom ??= DateTo.Value.AddDays(-30);
        if (DateFrom.Value > DateTo.Value)
        {
            (DateFrom, DateTo) = (DateTo, DateFrom);
        }

        var from = TimeZoneInfo.ConvertTimeToUtc(
            DateTime.SpecifyKind(DateFrom.Value.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified),
            MoscowTimeZone);
        var to = TimeZoneInfo.ConvertTimeToUtc(
            DateTime.SpecifyKind(DateTo.Value.AddDays(1).ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified),
            MoscowTimeZone);
        var records = await _db.ApiUsageRecords.AsNoTracking().Where(x => x.CreatedAt >= from && x.CreatedAt < to).ToListAsync();
        Categories = records
            .GroupBy(x => x.Category)
            .Select(x => new UsageRow(DisplayCategory(x.Key), x.Count(), x.Sum(y => y.InputTokens), x.Sum(y => y.OutputTokens), x.Sum(y => y.EstimatedCostRub)))
            .OrderByDescending(x => x.CostRub)
            .AsEnumerable()
            .Select((row, index) => row with { Color = DonutColors[index % DonutColors.Length] })
            .ToList();
        Models = records.GroupBy(x => $"{x.Provider} · {x.Model}").Select(x => new UsageRow(x.Key, x.Count(), x.Sum(y => y.InputTokens), x.Sum(y => y.OutputTokens), x.Sum(y => y.EstimatedCostRub))).OrderByDescending(x => x.CostRub).ToList();
        DonutStyle = BuildDonut(Categories);
    }

    public static string Money(decimal value) =>
        $"{value.ToString("F2", CultureInfo.InvariantCulture)} ₽";
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
        var total = rows.Sum(x => x.CostRub);
        if (total <= 0) return "#e8f1f8 0 100%";
        decimal cursor = 0;
        return string.Join(", ", rows.Select((row, i) => {
            var start = cursor; cursor += row.CostRub / total * 100;
            return string.Create(
                CultureInfo.InvariantCulture,
                $"{DonutColors[i % DonutColors.Length]} {start:F2}% {cursor:F2}%");
        }));
    }

    private static TimeZoneInfo ResolveMoscowTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Russian Standard Time");
        }
        catch
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById("Europe/Moscow");
            }
            catch
            {
                return TimeZoneInfo.CreateCustomTimeZone("Moscow", TimeSpan.FromHours(3), "Moscow", "Moscow");
            }
        }
    }

    public sealed record UsageRow(
        string Name,
        int RequestCount,
        int InputTokens,
        int OutputTokens,
        decimal CostRub,
        string? Color = null);
}
