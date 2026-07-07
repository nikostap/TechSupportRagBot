using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using TechSupportRagBot.Data;
using TechSupportRagBot.Models;
using TechSupportRagBot.Services;

namespace TechSupportRagBot.Pages.Client;

[Authorize(Roles = "Client,Admin")]
public class NewTicketModel : PageModel
{
    private readonly ApplicationDbContext _db;

    public NewTicketModel(ApplicationDbContext db) => _db = db;

    [BindProperty]
    public TicketInput Input { get; set; } = new();

    public SelectList MachineOptions { get; private set; } = new(Array.Empty<object>());

    public async Task OnGetAsync() => await LoadAsync();

    public async Task<IActionResult> OnPostAsync()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        var clientId = await _db.Users
            .Where(x => x.Id == userId)
            .Select(x => x.ClientId)
            .FirstOrDefaultAsync();

        var hasAccess = clientId != null
            && await _db.ClientMachines.AnyAsync(x => x.ClientId == clientId && x.MachineId == Input.MachineId);

        if (!hasAccess)
        {
            ModelState.AddModelError(string.Empty, "Нет доступа к выбранному станку.");
        }

        if (!ModelState.IsValid)
        {
            await LoadAsync();
            return Page();
        }

        var ticket = new Ticket
        {
            Title = TextEncodingRepairService.RepairIfNeeded(Input.Title).Trim(),
            MachineId = Input.MachineId,
            ClientUserId = userId,
            Status = TicketStatuses.New
        };

        _db.Tickets.Add(ticket);
        await _db.SaveChangesAsync();

        _db.ChatMessages.Add(new ChatMessage
        {
            TicketId = ticket.Id,
            AuthorUserId = userId,
            Text = TextEncodingRepairService.RepairIfNeeded(Input.Question).Trim(),
            IsReadByClient = true
        });

        await _db.SaveChangesAsync();
        return RedirectToPage("/Client/Ticket", new { id = ticket.Id });
    }

    private async Task LoadAsync()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var clientId = await _db.Users.Where(x => x.Id == userId).Select(x => x.ClientId).FirstOrDefaultAsync();
        var machines = clientId == null
            ? new List<Machine>()
            : await _db.ClientMachines
                .Include(x => x.Machine)
                .Where(x => x.ClientId == clientId)
                .Select(x => x.Machine!)
                .OrderBy(x => x.Name)
                .ToListAsync();

        MachineOptions = new SelectList(
            machines.Select(x => new
            {
                x.Id,
                DisplayName = string.Join(" · ", new[]
                {
                    x.Name,
                    string.IsNullOrWhiteSpace(x.Model) ? null : x.Model,
                    string.IsNullOrWhiteSpace(x.SerialNumber) ? null : $"SN: {x.SerialNumber}"
                }.Where(part => !string.IsNullOrWhiteSpace(part)))
            }),
            "Id",
            "DisplayName");
    }

    public class TicketInput
    {
        [Required]
        public int MachineId { get; set; }

        [Required]
        public string Title { get; set; } = string.Empty;

        [Required]
        public string Question { get; set; } = string.Empty;
    }
}
