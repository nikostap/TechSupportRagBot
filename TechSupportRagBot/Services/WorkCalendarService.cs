using Microsoft.EntityFrameworkCore;
using TechSupportRagBot.Data;
using TechSupportRagBot.Models;

namespace TechSupportRagBot.Services;

public class WorkCalendarService
{
    private static readonly TimeZoneInfo MoscowTimeZone = ResolveMoscowTimeZone();
    private readonly ApplicationDbContext _db;
    private readonly HttpClient _httpClient;

    public WorkCalendarService(ApplicationDbContext db, HttpClient httpClient)
    {
        _db = db;
        _httpClient = httpClient;
    }

    public async Task<IReadOnlyList<WorkCalendarPreviewDay>> PreviewRussianYearAsync(int year, CancellationToken cancellationToken)
    {
        ValidateYear(year);
        var response = await _httpClient.GetStringAsync(
            $"https://isdayoff.ru/api/getdata?year={year}&cc=ru&pre=1",
            cancellationToken);
        var codes = response.Where(char.IsDigit).ToArray();
        var expectedDays = DateTime.IsLeapYear(year) ? 366 : 365;
        if (codes.Length != expectedDays)
        {
            throw new InvalidOperationException($"Сервис вернул {codes.Length} дней вместо {expectedDays}.");
        }

        var result = new List<WorkCalendarPreviewDay>(expectedDays);
        var date = new DateOnly(year, 1, 1);
        for (var i = 0; i < codes.Length; i++, date = date.AddDays(1))
        {
            result.Add(new WorkCalendarPreviewDay(date, ParseDayType(codes[i])));
        }

        return result;
    }

    public async Task ImportAsync(int year, IReadOnlyCollection<WorkCalendarPreviewDay> days, CancellationToken cancellationToken)
    {
        ValidateYear(year);
        var supplied = days.Where(x => x.Date.Year == year).ToDictionary(x => x.Date);
        var existing = await _db.WorkCalendarDays.Where(x => x.Date.Year == year).ToListAsync(cancellationToken);

        foreach (var item in existing.Where(x => !x.IsManualOverride && !supplied.ContainsKey(x.Date)))
        {
            _db.WorkCalendarDays.Remove(item);
        }

        foreach (var preview in supplied.Values)
        {
            var item = existing.FirstOrDefault(x => x.Date == preview.Date);
            if (item?.IsManualOverride == true)
            {
                continue;
            }

            if (item == null)
            {
                item = new WorkCalendarDay { Date = preview.Date };
                _db.WorkCalendarDays.Add(item);
            }

            item.DayType = preview.DayType;
            item.Source = "isDayOff";
            item.IsManualOverride = false;
            item.UpdatedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync(cancellationToken);
        await ReclassifyNonWorkingEntriesAsync(
            supplied.Values.Where(x => x.DayType is WorkCalendarDayType.NonWorking or WorkCalendarDayType.Holiday).Select(x => x.Date),
            cancellationToken);
    }

    public async Task SaveManualAsync(DateOnly date, WorkCalendarDayType dayType, string? name, CancellationToken cancellationToken)
    {
        var item = await _db.WorkCalendarDays.FirstOrDefaultAsync(x => x.Date == date, cancellationToken);
        if (item == null)
        {
            item = new WorkCalendarDay { Date = date };
            _db.WorkCalendarDays.Add(item);
        }

        item.DayType = dayType;
        item.Name = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        item.Source = "Manual";
        item.IsManualOverride = true;
        item.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        if (!item.IsWorking)
        {
            await ReclassifyNonWorkingEntriesAsync([date], cancellationToken);
        }
    }

    public async Task<IReadOnlyDictionary<DateOnly, bool>> GetWorkingDaysAsync(
        DateOnly firstDate,
        DateOnly lastDate,
        CancellationToken cancellationToken)
    {
        return await _db.WorkCalendarDays
            .AsNoTracking()
            .Where(x => x.Date >= firstDate && x.Date <= lastDate)
            .ToDictionaryAsync(x => x.Date, x => x.DayType == WorkCalendarDayType.Working || x.DayType == WorkCalendarDayType.Shortened, cancellationToken);
    }

    public async Task<IReadOnlyList<WorkCalendarDay>> GetYearAsync(int year, CancellationToken cancellationToken) =>
        await _db.WorkCalendarDays.AsNoTracking().Where(x => x.Date.Year == year).OrderBy(x => x.Date).ToListAsync(cancellationToken);

    public static bool IsWorkingDay(DateOnly date, IReadOnlyDictionary<DateOnly, bool> overrides) =>
        overrides.TryGetValue(date, out var isWorking)
            ? isWorking
            : date.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday;

    private static WorkCalendarDayType ParseDayType(char code) => code switch
    {
        '0' or '4' => WorkCalendarDayType.Working,
        '2' => WorkCalendarDayType.Shortened,
        '8' => WorkCalendarDayType.Holiday,
        '1' => WorkCalendarDayType.NonWorking,
        _ => throw new InvalidOperationException($"Неизвестный код календаря: {code}")
    };

    private static void ValidateYear(int year)
    {
        if (year is < 2000 or > 2100)
        {
            throw new ArgumentOutOfRangeException(nameof(year));
        }
    }

    private async Task ReclassifyNonWorkingEntriesAsync(IEnumerable<DateOnly> dates, CancellationToken cancellationToken)
    {
        var nonWorkingDates = dates.ToHashSet();
        if (nonWorkingDates.Count == 0)
        {
            return;
        }

        var firstLocal = nonWorkingDates.Min().ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        var lastLocal = nonWorkingDates.Max().AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        var firstUtc = TimeZoneInfo.ConvertTimeToUtc(firstLocal, MoscowTimeZone);
        var lastUtc = TimeZoneInfo.ConvertTimeToUtc(lastLocal, MoscowTimeZone);
        var entries = await _db.OperatorChatTimeEntries
            .Where(x => x.StartedAt >= firstUtc && x.StartedAt < lastUtc && x.WorkSeconds > 0)
            .ToListAsync(cancellationToken);

        foreach (var entry in entries)
        {
            var localDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(
                DateTime.SpecifyKind(entry.StartedAt, DateTimeKind.Utc),
                MoscowTimeZone));
            if (!nonWorkingDates.Contains(localDate))
            {
                continue;
            }

            entry.OvertimeSeconds += entry.WorkSeconds;
            entry.WorkSeconds = 0;
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    private static TimeZoneInfo ResolveMoscowTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Russian Standard Time");
        }
        catch
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Europe/Moscow");
        }
    }
}

public record WorkCalendarPreviewDay(DateOnly Date, WorkCalendarDayType DayType);
