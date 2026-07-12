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
    private readonly AccessProfileService _access;

    public TicketsModel(
        ApplicationDbContext db,
        UserManager<ApplicationUser> userManager,
        TicketDeletionService ticketDeletion,
        AccessProfileService access)
    {
        _db = db;
        _userManager = userManager;
        _ticketDeletion = ticketDeletion;
        _access = access;
    }

    [BindProperty]
    public string? OperatorId { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? StatusFilter { get; set; }

    public List<Ticket> Tickets { get; private set; } = new();
    public List<ClientTicketGroup> ClientGroups { get; private set; } = new();
    public int TotalUnreadCount { get; private set; }

    public List<OperatorOption> OperatorOptions { get; private set; } = new();
    public bool CanAssignOperatorsToTickets { get; private set; }
    public bool CanDeleteTickets { get; private set; }

    public async Task OnGetAsync() => await LoadAsync();

    public async Task<IActionResult> OnPostAddOperatorAsync(int id)
    {
        if (!await _access.IsAllowedAsync(User, "AssignOperatorsToTickets", HttpContext.RequestAborted))
        {
            return Forbid();
        }

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
        if (!await _access.IsAllowedAsync(User, "AssignOperatorsToTickets", HttpContext.RequestAborted))
        {
            return Forbid();
        }

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
        if (!await _access.IsAllowedAsync(User, "DeleteTickets", HttpContext.RequestAborted))
        {
            return Forbid();
        }

        await _ticketDeletion.DeleteTicketAsync(id);
        return RedirectToPage();
    }

    private async Task LoadAsync()
    {
        CanAssignOperatorsToTickets = await _access.IsAllowedAsync(User, "AssignOperatorsToTickets", HttpContext.RequestAborted);
        CanDeleteTickets = await _access.IsAllowedAsync(User, "DeleteTickets", HttpContext.RequestAborted);

        var ticketsQuery = _db.Tickets
            .Include(x => x.Machine)
            .Include(x => x.ClientUser)
                .ThenInclude(x => x!.Client)
            .Include(x => x.OperatorUser)
            .Include(x => x.Messages)
            .Include(x => x.ResolvedAnswers)
            .Include(x => x.OperatorAssignments)
                .ThenInclude(x => x.OperatorUser)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(StatusFilter))
        {
            ticketsQuery = ticketsQuery.Where(x => x.Status == StatusFilter);
        }

        Tickets = await ticketsQuery
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync();

        var currentUserId = _userManager.GetUserId(User);
        var rows = Tickets
            .Select(ticket => new TicketRow(
                ticket,
                string.IsNullOrWhiteSpace(currentUserId)
                    ? 0
                    : ticket.Messages.Count(x =>
                        x.AuthorUserId != currentUserId &&
                        !x.IsBotMessage &&
                        !x.IsReadByOperator)))
            .ToList();

        TotalUnreadCount = rows.Sum(x => x.UnreadCount);
        ClientGroups = rows
            .GroupBy(x => new
            {
                x.Ticket.ClientUser?.ClientId,
                Name = x.Ticket.ClientUser?.Client?.Name
                    ?? x.Ticket.ClientUser?.FullName
                    ?? x.Ticket.ClientUser?.UserName
                    ?? UiText.T(HttpContext, "Client")
            })
            .OrderBy(x => x.Key.Name)
            .Select(clientGroup => new ClientTicketGroup(
                clientGroup.Key.Name,
                clientGroup.Sum(x => x.UnreadCount),
                clientGroup
                    .GroupBy(x => new
                    {
                        x.Ticket.MachineId,
                        Name = x.Ticket.Machine?.Name ?? UiText.T(HttpContext, "Machine")
                    })
                    .OrderBy(x => x.Key.Name)
                    .Select(machineGroup => new MachineTicketGroup(
                        machineGroup.Key.Name,
                        machineGroup.Sum(x => x.UnreadCount),
                        machineGroup.OrderByDescending(x => x.Ticket.CreatedAt).ToList()))
                    .ToList()))
            .ToList();

        var roleOperators = await _userManager.GetUsersInRoleAsync("Operator");
        var profileOperators = await _db.Users
            .Where(x => x.AccessProfile == AccessProfileService.Operator)
            .ToListAsync();

        OperatorOptions = roleOperators
            .Concat(profileOperators)
            .GroupBy(x => x.Id)
            .Select(x => x.First())
            .OrderBy(x => x.FullName ?? x.UserName)
            .Select(x => new OperatorOption(x.Id, x.FullName ?? x.UserName ?? x.Id))
            .ToList();
    }

    public sealed record OperatorOption(string Id, string Name);
    public sealed record TicketRow(Ticket Ticket, int UnreadCount);
    public sealed record MachineTicketGroup(string MachineName, int UnreadCount, List<TicketRow> Rows);
    public sealed record ClientTicketGroup(string ClientName, int UnreadCount, List<MachineTicketGroup> Machines)
    {
        public int TicketCount => Machines.Sum(x => x.Rows.Count);
    }
}
