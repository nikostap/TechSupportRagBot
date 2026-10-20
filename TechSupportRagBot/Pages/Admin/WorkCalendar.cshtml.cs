using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using TechSupportRagBot.Models;
using TechSupportRagBot.Services;

namespace TechSupportRagBot.Pages.Admin;

[Authorize]
public class WorkCalendarModel : PageModel
{
    private readonly WorkCalendarService _calendar;
    private readonly ILogger<WorkCalendarModel> _logger;

    public WorkCalendarModel(WorkCalendarService calendar, ILogger<WorkCalendarModel> logger)
    {
        _calendar = calendar;
        _logger = logger;
    }

    [BindProperty(SupportsGet = true)]
    [Range(2000, 2100)]
    public int Year { get; set; } = DateTime.Today.Year;

    [BindProperty]
    public ManualDayInput ManualDay { get; set; } = new();

    [BindProperty]
    public List<CalendarDayInput> PreviewDays { get; set; } = [];

    public IReadOnlyList<CalendarDisplayDay> Days { get; private set; } = [];
    public bool IsPreview { get; private set; }
    public string? StatusMessage { get; private set; }
    public string? ErrorMessage { get; private set; }

    public async Task OnGetAsync() => await LoadYearAsync();

    public async Task<IActionResult> OnPostPreviewAsync()
    {
        ModelState.Clear();
        try
        {
            PreviewDays = (await _calendar.PreviewRussianYearAsync(Year, HttpContext.RequestAborted))
                .Select(x => new CalendarDayInput { Date = x.Date, DayType = x.DayType })
                .ToList();
            IsPreview = true;
            StatusMessage = UiText.T(HttpContext, "CalendarPreviewReady");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to preview work calendar for {Year}", Year);
            ErrorMessage = UiText.T(HttpContext, "CalendarPreviewFailed");
        }

        await LoadYearAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostConfirmImportAsync()
    {
        try
        {
            var days = PreviewDays.Select(x => new WorkCalendarPreviewDay(x.Date, x.DayType)).ToArray();
            var expected = DateTime.IsLeapYear(Year) ? 366 : 365;
            if (days.Length != expected || days.Any(x => x.Date.Year != Year))
            {
                throw new InvalidOperationException("Предварительные данные неполные. Выполните импорт заново.");
            }

            await _calendar.ImportAsync(Year, days, HttpContext.RequestAborted);
            StatusMessage = $"{UiText.T(HttpContext, "CalendarSavedPrefix")} {Year} {UiText.T(HttpContext, "CalendarSavedSuffix")}";
            PreviewDays = [];
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save work calendar for {Year}", Year);
            ErrorMessage = UiText.T(HttpContext, "CalendarSaveFailed");
            IsPreview = true;
        }

        await LoadYearAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostSaveDayAsync()
    {
        Year = ManualDay.Date.Year;
        if (!ModelState.IsValid)
        {
            await LoadYearAsync();
            return Page();
        }

        await _calendar.SaveManualAsync(
            ManualDay.Date,
            ManualDay.DayType,
            ManualDay.Name,
            HttpContext.RequestAborted);
        StatusMessage = $"{UiText.T(HttpContext, "DaySavedPrefix")} {ManualDay.Date:dd.MM.yyyy} {UiText.T(HttpContext, "DaySavedSuffix")}";
        await LoadYearAsync();
        return Page();
    }

    private async Task LoadYearAsync()
    {
        if (Year is < 2000 or > 2100)
        {
            Year = DateTime.Today.Year;
        }

        var stored = (await _calendar.GetYearAsync(Year, HttpContext.RequestAborted)).ToDictionary(x => x.Date);
        var preview = IsPreview
            ? PreviewDays.Where(x => x.Date.Year == Year).ToDictionary(x => x.Date)
            : new Dictionary<DateOnly, CalendarDayInput>();
        var first = new DateOnly(Year, 1, 1);
        var count = DateTime.IsLeapYear(Year) ? 366 : 365;
        Days = Enumerable.Range(0, count).Select(offset =>
        {
            var date = first.AddDays(offset);
            stored.TryGetValue(date, out var item);
            preview.TryGetValue(date, out var previewDay);
            var defaultType = date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday
                ? WorkCalendarDayType.NonWorking
                : WorkCalendarDayType.Working;
            var type = item?.IsManualOverride == true
                ? item.DayType
                : previewDay?.DayType ?? item?.DayType ?? defaultType;
            var source = item?.IsManualOverride == true
                ? item.Source
                : previewDay != null ? UiText.T(HttpContext, "CalendarPreviewSource") : item?.Source ?? UiText.T(HttpContext, "ByDefault");
            return new CalendarDisplayDay(date, type, source, item?.IsManualOverride == true, item?.Name);
        }).ToArray();
    }

    public class ManualDayInput
    {
        [Required]
        public DateOnly Date { get; set; } = DateOnly.FromDateTime(DateTime.Today);
        public WorkCalendarDayType DayType { get; set; } = WorkCalendarDayType.NonWorking;
        [StringLength(200)]
        public string? Name { get; set; }
    }

    public class CalendarDayInput
    {
        public DateOnly Date { get; set; }
        public WorkCalendarDayType DayType { get; set; }
    }

    public record CalendarDisplayDay(DateOnly Date, WorkCalendarDayType DayType, string Source, bool IsManual, string? Name);
}
