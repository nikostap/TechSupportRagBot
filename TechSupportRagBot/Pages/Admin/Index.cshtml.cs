using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using TechSupportRagBot.Data;
using TechSupportRagBot.Models;

namespace TechSupportRagBot.Pages.Admin;

[Authorize(Roles = "Admin")]
public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _db;

    public IndexModel(ApplicationDbContext db) => _db = db;

    public int ClientCount { get; private set; }
    public int MachineCount { get; private set; }
    public int LicenseCount { get; private set; }
    public int OpenTicketCount { get; private set; }

    public async Task OnGetAsync()
    {
        ClientCount = await _db.Clients.CountAsync();
        MachineCount = await _db.Machines.CountAsync();
        LicenseCount = await _db.Licenses.CountAsync();
        OpenTicketCount = await _db.Tickets.CountAsync(x => x.Status != TicketStatuses.Closed);
    }
}
