using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using TechSupportRagBot.Data;
using TechSupportRagBot.Models;

namespace TechSupportRagBot.Pages.Client;

[Authorize(Roles = "Client,Admin")]
public class MachinesModel : PageModel
{
    private readonly ApplicationDbContext _db;

    public MachinesModel(ApplicationDbContext db) => _db = db;

    public List<TechSupportRagBot.Models.ClientMachine> Machines { get; private set; } = new();

    public async Task OnGetAsync()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var clientId = await _db.Users.Where(x => x.Id == userId).Select(x => x.ClientId).FirstOrDefaultAsync();

        Machines = clientId == null
            ? new List<TechSupportRagBot.Models.ClientMachine>()
            : await _db.ClientMachines
                .Include(x => x.Machine)
                .Where(x => x.ClientId == clientId)
                .OrderBy(x => x.Machine!.Name)
                .ToListAsync();
    }
}
