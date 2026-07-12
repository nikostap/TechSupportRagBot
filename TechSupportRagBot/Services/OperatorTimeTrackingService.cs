using Microsoft.EntityFrameworkCore;
using TechSupportRagBot.Data;
using TechSupportRagBot.Models;

namespace TechSupportRagBot.Services;

public class OperatorTimeTrackingService
{
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan OnlineWindow = TimeSpan.FromMinutes(2);
    private static readonly TimeZoneInfo MoscowTimeZone = ResolveMoscowTimeZone();

    private readonly ApplicationDbContext _db;
    private readonly WorkCalendarService _workCalendar;
    private readonly ILogger<OperatorTimeTrackingService> _logger;

    public OperatorTimeTrackingService(
        ApplicationDbContext db,
        WorkCalendarService workCalendar,
        ILogger<OperatorTimeTrackingService> logger)
    {
        _db = db;
        _workCalendar = workCalendar;
        _logger = logger;
    }

    public async Task<bool> StartSessionAsync(string operatorUserId, int ticketId, CancellationToken cancellationToken = default)
    {
        return await TouchAsync(operatorUserId, ticketId, removePresence: false, cancellationToken);
    }

    public async Task<bool> TrackActivityAsync(string operatorUserId, int ticketId, CancellationToken cancellationToken = default)
    {
        return await TouchAsync(operatorUserId, ticketId, removePresence: false, cancellationToken);
    }

    public async Task<bool> EndSessionAsync(string operatorUserId, int ticketId, CancellationToken cancellationToken = default)
    {
        return await TouchAsync(operatorUserId, ticketId, removePresence: true, cancellationToken);
    }

    public static DateTime ActiveSinceUtc => DateTime.UtcNow.Subtract(OnlineWindow);

    private async Task<bool> TouchAsync(string operatorUserId, int ticketId, bool removePresence, CancellationToken cancellationToken)
    {
        var ticket = await _db.Tickets
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == ticketId, cancellationToken);

        if (ticket == null)
        {
            _logger.LogDebug("Operator time tracking skipped. Ticket={TicketId}, Reason=TicketMissing", ticketId);
            return false;
        }

        var now = DateTime.UtcNow;
        var user = await _db.Users.FirstOrDefaultAsync(x => x.Id == operatorUserId, cancellationToken);
        if (user == null)
        {
            _logger.LogDebug("Operator time tracking skipped. Operator={OperatorUserId}, Reason=UserMissing", operatorUserId);
            return false;
        }

        var presence = await _db.OperatorChatPresences
            .FirstOrDefaultAsync(x => x.OperatorUserId == operatorUserId, cancellationToken);

        if (removePresence && presence?.TicketId != ticketId)
        {
            _logger.LogDebug(
                "Operator time end skipped. Ticket={TicketId}, Operator={OperatorUserId}, Reason=PresenceBelongsToAnotherTicket",
                ticketId,
                operatorUserId);
            return false;
        }

        if (ticket.Status == TicketStatuses.Closed && !removePresence)
        {
            _logger.LogDebug("Operator time tracking skipped. Ticket={TicketId}, Reason=TicketClosed", ticketId);
            return false;
        }

        var entryCreated = false;
        if (presence != null)
        {
            entryCreated = await ClosePresenceIntervalAsync(presence, user, now, cancellationToken);
        }

        if (removePresence)
        {
            if (presence != null)
            {
                _db.OperatorChatPresences.Remove(presence);
            }
        }
        else if (presence == null)
        {
            _db.OperatorChatPresences.Add(new OperatorChatPresence
            {
                OperatorUserId = operatorUserId,
                TicketId = ticketId,
                LastActivityAt = now
            });
        }
        else
        {
            presence.TicketId = ticketId;
            presence.LastActivityAt = now;
        }

        await _db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation(
            "Operator time activity tracked. Ticket={TicketId}, Operator={OperatorUserId}, EntryCreated={EntryCreated}, EndSession={EndSession}",
            ticketId,
            operatorUserId,
            entryCreated,
            removePresence);
        return entryCreated;
    }

    private async Task<bool> ClosePresenceIntervalAsync(
        OperatorChatPresence presence,
        ApplicationUser user,
        DateTime now,
        CancellationToken cancellationToken)
    {
        if (now <= presence.LastActivityAt || now - presence.LastActivityAt > IdleTimeout)
        {
            return false;
        }

        var previousTicket = await _db.Tickets
            .AsNoTracking()
            .Include(x => x.Machine)
            .FirstOrDefaultAsync(x => x.Id == presence.TicketId, cancellationToken);
        if (previousTicket == null)
        {
            return false;
        }

        var startLocal = ToMoscowTime(presence.LastActivityAt);
        var endLocal = ToMoscowTime(now);
        var calendar = await _workCalendar.GetWorkingDaysAsync(
            DateOnly.FromDateTime(startLocal),
            DateOnly.FromDateTime(endLocal),
            cancellationToken);
        var split = SplitWorkAndOvertime(
            presence.LastActivityAt,
            now,
            user.WorkdayStartMinutes,
            user.WorkdayEndMinutes,
            calendar);
        if (split.workSeconds <= 0 && split.overtimeSeconds <= 0)
        {
            return false;
        }

        _db.OperatorChatTimeEntries.Add(new OperatorChatTimeEntry
        {
            OperatorUserId = presence.OperatorUserId,
            TicketId = presence.TicketId,
            MachineId = previousTicket.MachineId,
            OperatorName = user.FullName ?? user.UserName ?? presence.OperatorUserId,
            MachineModel = previousTicket.Machine?.Model ?? string.Empty,
            TicketReference = $"#{previousTicket.Id} {previousTicket.Title}",
            StartedAt = presence.LastActivityAt,
            EndedAt = now,
            WorkSeconds = split.workSeconds,
            OvertimeSeconds = split.overtimeSeconds
        });
        return true;
    }

    private static (int workSeconds, int overtimeSeconds) SplitWorkAndOvertime(
        DateTime startUtc,
        DateTime endUtc,
        int workdayStartMinutes,
        int workdayEndMinutes,
        IReadOnlyDictionary<DateOnly, bool> calendar)
    {
        if (endUtc <= startUtc)
        {
            return (0, 0);
        }

        var startLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(startUtc, DateTimeKind.Utc), MoscowTimeZone);
        var endLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(endUtc, DateTimeKind.Utc), MoscowTimeZone);

        var work = 0;
        var overtime = 0;
        var cursor = startLocal;
        while (cursor < endLocal)
        {
            var next = cursor.AddSeconds(30);
            if (next > endLocal)
            {
                next = endLocal;
            }

            var midpoint = cursor.AddTicks((next - cursor).Ticks / 2);
            var minuteOfDay = midpoint.Hour * 60 + midpoint.Minute;
            var seconds = (int)Math.Round((next - cursor).TotalSeconds);
            var date = DateOnly.FromDateTime(midpoint);
            if (WorkCalendarService.IsWorkingDay(date, calendar)
                && IsInsideWorkday(minuteOfDay, workdayStartMinutes, workdayEndMinutes))
            {
                work += seconds;
            }
            else
            {
                overtime += seconds;
            }

            cursor = next;
        }

        return (work, overtime);
    }

    private static bool IsInsideWorkday(int minuteOfDay, int startMinutes, int endMinutes)
    {
        if (startMinutes == endMinutes)
        {
            return true;
        }

        return startMinutes < endMinutes
            ? minuteOfDay >= startMinutes && minuteOfDay < endMinutes
            : minuteOfDay >= startMinutes || minuteOfDay < endMinutes;
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

    private static DateTime ToMoscowTime(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), MoscowTimeZone);
}
