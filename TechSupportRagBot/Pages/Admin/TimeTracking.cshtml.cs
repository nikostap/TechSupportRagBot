using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using TechSupportRagBot.Data;
using TechSupportRagBot.Models;

namespace TechSupportRagBot.Pages.Admin;

[Authorize(Roles = "Admin")]
public class TimeTrackingModel : PageModel
{
    private static readonly TimeZoneInfo MoscowTimeZone = ResolveMoscowTimeZone();
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;

    public TimeTrackingModel(ApplicationDbContext db, UserManager<ApplicationUser> userManager)
    {
        _db = db;
        _userManager = userManager;
    }

    [BindProperty(SupportsGet = true)]
    public DateTime? DateFrom { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateTime? DateTo { get; set; }

    [BindProperty(SupportsGet = true)]
    public string Period { get; set; } = "day";

    [BindProperty(SupportsGet = true)]
    public string? OperatorId { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? MachineModel { get; set; }

    public SelectList OperatorOptions { get; private set; } = new(Array.Empty<object>());

    public SelectList MachineModelOptions { get; private set; } = new(Array.Empty<object>());

    public List<TimeReportRow> Rows { get; private set; } = new();

    public List<ChartRow> OperatorChart { get; private set; } = new();

    public List<ChartRow> MachineChart { get; private set; } = new();

    public int TotalWorkSeconds { get; private set; }

    public int TotalOvertimeSeconds { get; private set; }

    public int MaxOperatorSeconds => Math.Max(1, OperatorChart.Count == 0 ? 1 : OperatorChart.Max(x => x.TotalSeconds));

    public int MaxMachineSeconds => Math.Max(1, MachineChart.Count == 0 ? 1 : MachineChart.Max(x => x.TotalSeconds));

    public async Task OnGetAsync()
    {
        DateTo ??= DateTime.Today;
        DateFrom ??= DateTo.Value.AddDays(-30);

        var fromUtc = TimeZoneInfo.ConvertTimeToUtc(
            DateTime.SpecifyKind(DateFrom.Value.Date, DateTimeKind.Unspecified),
            MoscowTimeZone);
        var toUtc = TimeZoneInfo.ConvertTimeToUtc(
            DateTime.SpecifyKind(DateTo.Value.Date.AddDays(1), DateTimeKind.Unspecified),
            MoscowTimeZone);

        var query = _db.OperatorChatTimeEntries
            .AsNoTracking()
            .Include(x => x.OperatorUser)
            .Include(x => x.Machine)
            .Include(x => x.Ticket)
            .Where(x => x.StartedAt >= fromUtc && x.StartedAt < toUtc);

        if (!string.IsNullOrWhiteSpace(OperatorId))
        {
            query = query.Where(x => x.OperatorUserId == OperatorId);
        }

        if (!string.IsNullOrWhiteSpace(MachineModel))
        {
            query = query.Where(x => x.Machine != null && x.Machine.Model == MachineModel);
        }

        var entries = await query.ToListAsync();
        TotalWorkSeconds = entries.Sum(x => x.WorkSeconds);
        TotalOvertimeSeconds = entries.Sum(x => x.OvertimeSeconds);

        Rows = entries
            .GroupBy(x => new
            {
                Period = PeriodKey(x.StartedAt),
                x.OperatorUserId,
                OperatorName = x.OperatorUser!.FullName ?? x.OperatorUser.UserName ?? x.OperatorUserId,
                MachineModel = x.Machine!.Model
            })
            .Select(x => new TimeReportRow(
                x.Key.Period,
                x.Key.OperatorName,
                x.Key.MachineModel,
                x.Select(e => e.TicketId).Distinct().Count(),
                x.Sum(e => e.WorkSeconds),
                x.Sum(e => e.OvertimeSeconds)))
            .OrderByDescending(x => x.Period)
            .ThenBy(x => x.OperatorName)
            .ThenBy(x => x.MachineModel)
            .ToList();

        OperatorChart = entries
            .GroupBy(x => x.OperatorUser!.FullName ?? x.OperatorUser.UserName ?? x.OperatorUserId)
            .Select(x => new ChartRow(x.Key, x.Sum(e => e.WorkSeconds), x.Sum(e => e.OvertimeSeconds)))
            .OrderByDescending(x => x.TotalSeconds)
            .ToList();

        MachineChart = entries
            .GroupBy(x => string.IsNullOrWhiteSpace(x.Machine!.Model) ? "Без модели" : x.Machine.Model)
            .Select(x => new ChartRow(x.Key, x.Sum(e => e.WorkSeconds), x.Sum(e => e.OvertimeSeconds)))
            .OrderByDescending(x => x.TotalSeconds)
            .ToList();

        var operators = await _userManager.GetUsersInRoleAsync("Operator");
        OperatorOptions = new SelectList(
            operators.OrderBy(x => x.FullName ?? x.UserName).Select(x => new
            {
                x.Id,
                Name = x.FullName ?? x.UserName ?? x.Id
            }),
            "Id",
            "Name",
            OperatorId);

        var models = await _db.Machines
            .AsNoTracking()
            .Where(x => x.Model != "")
            .Select(x => x.Model)
            .Distinct()
            .OrderBy(x => x)
            .ToListAsync();
        MachineModelOptions = new SelectList(models, MachineModel);
    }

    public static string FormatDuration(int seconds)
    {
        var span = TimeSpan.FromSeconds(seconds);
        return span.TotalHours >= 1
            ? $"{(int)span.TotalHours} ч {span.Minutes:00} мин"
            : $"{span.Minutes} мин {span.Seconds:00} сек";
    }

    public int Percent(int seconds, int maxSeconds)
    {
        return Math.Clamp((int)Math.Round(seconds * 100.0 / Math.Max(1, maxSeconds)), 1, 100);
    }

    private string PeriodKey(DateTime utc)
    {
        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), MoscowTimeZone);
        return Period switch
        {
            "year" => local.ToString("yyyy"),
            "month" => local.ToString("yyyy-MM"),
            _ => local.ToString("yyyy-MM-dd")
        };
    }

    private static TimeZoneInfo ResolveMoscowTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Russian Standard Time");
        }
        catch
        {
            return TimeZoneInfo.Local;
        }
    }

    public sealed record TimeReportRow(
        string Period,
        string OperatorName,
        string MachineModel,
        int TicketCount,
        int WorkSeconds,
        int OvertimeSeconds)
    {
        public int TotalSeconds => WorkSeconds + OvertimeSeconds;
    }

    public sealed record ChartRow(string Name, int WorkSeconds, int OvertimeSeconds)
    {
        public int TotalSeconds => WorkSeconds + OvertimeSeconds;
    }
}
