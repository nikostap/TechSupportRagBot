using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using TechSupportRagBot.Data;
using TechSupportRagBot.Models;
using TechSupportRagBot.Services;

namespace TechSupportRagBot.Pages.Admin;

[Authorize(Roles = "Admin")]
public class TicketsModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly TicketDeletionService _ticketDeletion;

    public TicketsModel(
        ApplicationDbContext db,
        UserManager<ApplicationUser> userManager,
        TicketDeletionService ticketDeletion)
    {
        _db = db;
        _userManager = userManager;
        _ticketDeletion = ticketDeletion;
    }

    [BindProperty]
    public string? OperatorId { get; set; }

    public List<Ticket> Tickets { get; private set; } = new();

    public List<OperatorOption> OperatorOptions { get; private set; } = new();

    public async Task OnGetAsync() => await LoadAsync();

    public async Task<IActionResult> OnPostAddOperatorAsync(int id)
    {
        if (string.IsNullOrWhiteSpace(OperatorId))
        {
            return RedirectToPage();
        }

        var ticket = await _db.Tickets
            .Include(x => x.OperatorAssignments)
            .FirstOrDefaultAsync(x => x.Id == id);

        if (ticket != null && !ticket.OperatorAssignments.Any(x => x.OperatorUserId == OperatorId))
        {
            _db.TicketOperatorAssignments.Add(new TicketOperatorAssignment
            {
                TicketId = id,
                OperatorUserId = OperatorId
            });

            if (ticket.Status != TicketStatuses.Closed)
            {
                ticket.Status = TicketStatuses.WaitingForOperator;
            }

            await _db.SaveChangesAsync();
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRemoveOperatorAsync(int id, string operatorId)
    {
        var assignment = await _db.TicketOperatorAssignments
            .FirstOrDefaultAsync(x => x.TicketId == id && x.OperatorUserId == operatorId);

        if (assignment != null)
        {
            _db.TicketOperatorAssignments.Remove(assignment);
            await _db.SaveChangesAsync();
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        await _ticketDeletion.DeleteTicketAsync(id);
        return RedirectToPage();
    }

    private async Task LoadAsync()
    {
        Tickets = await _db.Tickets
            .Include(x => x.Machine)
            .Include(x => x.ClientUser)
            .Include(x => x.OperatorUser)
            .Include(x => x.ResolvedAnswers)
            .Include(x => x.OperatorAssignments)
                .ThenInclude(x => x.OperatorUser)
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync();

        var operators = await _userManager.GetUsersInRoleAsync("Operator");
        OperatorOptions = operators
            .OrderBy(x => x.FullName ?? x.UserName)
            .Select(x => new OperatorOption(x.Id, x.FullName ?? x.UserName ?? x.Id))
            .ToList();
    }

    public sealed record OperatorOption(string Id, string Name);
}
