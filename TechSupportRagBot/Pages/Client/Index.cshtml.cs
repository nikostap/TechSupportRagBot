using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using TechSupportRagBot.Data;
using TechSupportRagBot.Models;

namespace TechSupportRagBot.Pages.Client;

[Authorize(Roles = "Client,Admin")]
public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _db;

    public IndexModel(ApplicationDbContext db) => _db = db;

    public int AvailableMachineCount { get; private set; }
    public int OpenTicketCount { get; private set; }
    public int UnreadMessageCount { get; private set; }
    public int CompanyUserCount { get; private set; }
    public bool HasClient { get; private set; }
    public List<Ticket> RecentTickets { get; private set; } = new();
    public HashSet<int> TicketsWithUnreadMessages { get; private set; } = new();

    public async Task OnGetAsync()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var clientId = await _db.Users.Where(x => x.Id == userId).Select(x => x.ClientId).FirstOrDefaultAsync();
        HasClient = clientId != null;

        AvailableMachineCount = clientId == null
            ? 0
            : await _db.ClientMachines.CountAsync(x => x.ClientId == clientId);

        OpenTicketCount = await _db.Tickets.CountAsync(x => x.ClientUserId == userId && x.Status != TicketStatuses.Closed);

        UnreadMessageCount = await _db.ChatMessages
            .Include(x => x.Ticket)
            .CountAsync(x =>
                x.Ticket!.ClientUserId == userId &&
                x.AuthorUserId != userId &&
                !x.IsReadByClient);

        CompanyUserCount = clientId == null
            ? 0
            : await _db.Users.CountAsync(x => x.ClientId == clientId);

        RecentTickets = await _db.Tickets
            .Include(x => x.Machine)
            .Where(x => x.ClientUserId == userId)
            .OrderByDescending(x => x.CreatedAt)
            .Take(8)
            .ToListAsync();

        TicketsWithUnreadMessages = await _db.ChatMessages
            .Include(x => x.Ticket)
            .Where(x =>
                x.Ticket!.ClientUserId == userId &&
                x.AuthorUserId != userId &&
                !x.IsReadByClient)
            .Select(x => x.TicketId)
            .ToHashSetAsync();
    }

    public string StatusName(string status) => status switch
    {
        TicketStatuses.New => "Новый",
        TicketStatuses.BotAnswered => "Ответ бота",
        TicketStatuses.WaitingForOperator => "Ждет оператора",
        TicketStatuses.InProgress => "В работе",
        TicketStatuses.Closed => "Закрыт",
        _ => status
    };
}
