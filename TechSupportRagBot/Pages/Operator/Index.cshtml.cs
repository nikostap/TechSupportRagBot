using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using TechSupportRagBot.Data;
using TechSupportRagBot.Models;

namespace TechSupportRagBot.Pages.Operator;

[Authorize(Roles = "Operator,Admin")]
public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _db;

    public IndexModel(ApplicationDbContext db) => _db = db;

    public List<TicketGroup> TicketGroups { get; private set; } = new();

    public async Task OnGetAsync()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        var tickets = await _db.Tickets
            .Include(x => x.Machine)
            .Include(x => x.ClientUser)
            .Include(x => x.OperatorAssignments)
            .Include(x => x.Messages)
            .Where(x => x.Status != TicketStatuses.Closed)
            .Where(x => x.OperatorUserId == userId
                || x.OperatorAssignments.Any(a => a.OperatorUserId == userId))
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync();

        TicketGroups = tickets
            .Select(ticket => new TicketRow(
                ticket,
                ticket.Status == TicketStatuses.WaitingForOperator
                    && !ticket.Messages.Any(x => x.AuthorUserId == userId && !x.IsBotMessage),
                string.IsNullOrWhiteSpace(userId)
                    ? 0
                    : ticket.Messages.Count(x =>
                        x.AuthorUserId != userId &&
                        !x.IsReadByOperator)))
            .GroupBy(x => new
            {
                MachineId = x.Ticket.MachineId,
                Name = x.Ticket.Machine?.Name ?? "Machine"
            })
            .OrderBy(x => x.Key.Name)
            .Select(x => new TicketGroup(x.Key.Name, x.OrderByDescending(r => r.UnreadCount > 0).ThenByDescending(r => r.Ticket.CreatedAt).ToList()))
            .ToList();
    }

    public sealed record TicketRow(Ticket Ticket, bool IsNewAssigned, int UnreadCount);
    public sealed record TicketGroup(string MachineName, List<TicketRow> Rows);
}
