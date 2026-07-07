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

    public List<Ticket> Tickets { get; private set; } = new();

    public async Task OnGetAsync()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        Tickets = await _db.Tickets
            .Include(x => x.Machine)
            .Where(x => x.ClientUserId == userId)
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync();
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
