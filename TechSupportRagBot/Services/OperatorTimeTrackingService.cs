using Microsoft.EntityFrameworkCore;
using TechSupportRagBot.Data;
using TechSupportRagBot.Models;

namespace TechSupportRagBot.Services;

public class OperatorTimeTrackingService
{
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(5);
    private static readonly TimeZoneInfo MoscowTimeZone = ResolveMoscowTimeZone();

    private readonly ApplicationDbContext _db;

    public OperatorTimeTrackingService(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task TrackActivityAsync(string operatorUserId, int ticketId, CancellationToken cancellationToken = default)
    {
        var ticket = await _db.Tickets
            .AsNoTracking()
            .Include(x => x.OperatorAssignments)
            .FirstOrDefaultAsync(x => x.Id == ticketId, cancellationToken);

        if (ticket == null || ticket.Status == TicketStatuses.Closed)
        {
            return;
        }

        var isAssigned = ticket.OperatorUserId == operatorUserId
            || ticket.OperatorAssignments.Any(x => x.OperatorUserId == operatorUserId);
        if (!isAssigned)
        {
            return;
        }

        var now = DateTime.UtcNow;
        var user = await _db.Users.FirstOrDefaultAsync(x => x.Id == operatorUserId, cancellationToken);
        if (user == null)
        {
            return;
        }

        var presence = await _db.OperatorChatPresences
            .FirstOrDefaultAsync(x => x.OperatorUserId == operatorUserId, cancellationToken);

        if (presence != null
            && presence.TicketId == ticketId
            && now > presence.LastActivityAt
            && now - presence.LastActivityAt <= IdleTimeout)
        {
            var split = SplitWorkAndOvertime(presence.LastActivityAt, now, user.WorkdayStartMinutes, user.WorkdayEndMinutes);
            if (split.workSeconds > 0 || split.overtimeSeconds > 0)
            {
                _db.OperatorChatTimeEntries.Add(new OperatorChatTimeEntry
                {
                    OperatorUserId = operatorUserId,
                    TicketId = ticketId,
                    MachineId = ticket.MachineId,
                    StartedAt = presence.LastActivityAt,
                    EndedAt = now,
                    WorkSeconds = split.workSeconds,
                    OvertimeSeconds = split.overtimeSeconds
                });
            }
        }

        if (presence == null)
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
    }

    private static (int workSeconds, int overtimeSeconds) SplitWorkAndOvertime(
        DateTime startUtc,
        DateTime endUtc,
        int workdayStartMinutes,
        int workdayEndMinutes)
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
            if (IsInsideWorkday(minuteOfDay, workdayStartMinutes, workdayEndMinutes))
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
}
