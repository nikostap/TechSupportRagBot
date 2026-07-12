using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using TechSupportRagBot.Data;
using TechSupportRagBot.Models;

namespace TechSupportRagBot.Pages.Client;

[Authorize(Roles = "Client,Admin")]
public class TicketsModel : PageModel
{
    private readonly ApplicationDbContext _db;

    public TicketsModel(ApplicationDbContext db) => _db = db;

    public List<TicketGroup> TicketGroups { get; private set; } = new();
    public int TotalTicketCount { get; private set; }
    public int TotalUnreadCount { get; private set; }

    public async Task OnGetAsync()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var tickets = await _db.Tickets
            .Include(x => x.Machine)
            .Include(x => x.Messages)
            .Where(x => x.ClientUserId == userId)
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync();

        TotalTicketCount = tickets.Count;

        TicketGroups = tickets
            .Select(ticket => new TicketRow(
                ticket,
                string.IsNullOrWhiteSpace(userId)
                    ? 0
                    : ticket.Messages.Count(x =>
                        x.AuthorUserId != userId &&
                        !x.IsReadByClient)))
            .GroupBy(x => new
            {
                MachineId = x.Ticket.MachineId,
                Name = x.Ticket.Machine?.Name ?? "Machine"
            })
            .OrderBy(x => x.Key.Name)
            .Select(x =>
            {
                var rows = x.OrderByDescending(r => r.Ticket.CreatedAt).ToList();
                return new TicketGroup(x.Key.MachineId, x.Key.Name, rows.Sum(r => r.UnreadCount), rows);
            })
            .ToList();

        TotalUnreadCount = TicketGroups.Sum(x => x.UnreadCount);
    }

    public sealed record TicketRow(Ticket Ticket, int UnreadCount);
    public sealed record TicketGroup(int MachineId, string MachineName, int UnreadCount, List<TicketRow> Rows);
}
