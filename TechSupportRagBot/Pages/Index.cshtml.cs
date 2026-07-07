using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using TechSupportRagBot.Data;
using TechSupportRagBot.Models;
using TechSupportRagBot.Services;

namespace TechSupportRagBot.Pages
{
    public class IndexModel : PageModel
    {
        private readonly ApplicationDbContext _db;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly AccessProfileService _access;

        public IndexModel(
            ApplicationDbContext db,
            UserManager<ApplicationUser> userManager,
            AccessProfileService access)
        {
            _db = db;
            _userManager = userManager;
            _access = access;
        }

        public string DisplayName { get; private set; } = string.Empty;
        public string ProfileName { get; private set; } = string.Empty;
        public Dictionary<string, bool> Permissions { get; private set; } = new();

        public int ClientCount { get; private set; }
        public int MachineCount { get; private set; }
        public int LicenseCount { get; private set; }
        public int OpenTicketCount { get; private set; }
        public int ClientOpenTicketCount { get; private set; }
        public int WaitingForOperatorCount { get; private set; }
        public int AssignedTicketCount { get; private set; }
        public int UnreadMessageCount { get; private set; }
        public int AvailableMachineCount { get; private set; }
        public int CompanyUserCount { get; private set; }

        public List<Ticket> AttentionTickets { get; private set; } = new();
        public List<Ticket> AssignedTickets { get; private set; } = new();
        public List<Ticket> RecentClientTickets { get; private set; } = new();
        public HashSet<int> ClientTicketsWithUnreadMessages { get; private set; } = new();
        public bool HasClientAccount { get; private set; }
        public bool ShowOperatorArea { get; private set; }
        public bool IsInternalUser { get; private set; }

        public async Task<IActionResult> OnGetAsync()
        {
            if (!User.Identity?.IsAuthenticated ?? true)
            {
                return Page();
            }

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var user = await _userManager.GetUserAsync(User);
            DisplayName = user?.FullName ?? user?.UserName ?? User.Identity?.Name ?? string.Empty;
            ProfileName = user == null
                ? AccessProfileService.DisplayProfile(HttpContext, null)
                : AccessProfileService.DisplayProfile(HttpContext, await _access.ResolveProfileKeyAsync(user, HttpContext.RequestAborted));
            ShowOperatorArea = User.IsInRole("Operator");
            IsInternalUser = User.IsInRole("Admin");

            foreach (var permission in AccessProfileService.PermissionDefinitions)
            {
                Permissions[permission.Key] = await _access.IsAllowedAsync(User, permission.Key, HttpContext.RequestAborted);
            }

            await LoadAdminMetricsAsync();
            await LoadClientMetricsAsync(userId);
            await LoadOperatorMetricsAsync(userId);
            return Page();
        }

        public bool Can(string permission) =>
            Permissions.TryGetValue(permission, out var allowed) && allowed;

        public string StatusName(string status) => UiText.Status(HttpContext, status);

        private async Task LoadAdminMetricsAsync()
        {
            if (Can("ManageClients"))
            {
                ClientCount = await _db.Clients.CountAsync(HttpContext.RequestAborted);
            }

            if (Can("ManageMachines"))
            {
                MachineCount = await _db.Machines.CountAsync(HttpContext.RequestAborted);
            }

            if (Can("ManageLicenses"))
            {
                LicenseCount = await _db.Licenses.CountAsync(HttpContext.RequestAborted);
            }

            if (Can("Tickets"))
            {
                OpenTicketCount = await _db.Tickets.CountAsync(x => x.Status != TicketStatuses.Closed, HttpContext.RequestAborted);
                WaitingForOperatorCount = await _db.Tickets.CountAsync(x => x.Status == TicketStatuses.WaitingForOperator, HttpContext.RequestAborted);
                AttentionTickets = await _db.Tickets
                    .Include(x => x.Machine)
                    .Include(x => x.ClientUser)
                    .Where(x => x.Status != TicketStatuses.Closed)
                    .OrderByDescending(x => x.Status == TicketStatuses.WaitingForOperator)
                    .ThenByDescending(x => x.CreatedAt)
                    .Take(5)
                    .ToListAsync(HttpContext.RequestAborted);
            }
        }

        private async Task LoadClientMetricsAsync(string? userId)
        {
            if (string.IsNullOrWhiteSpace(userId) || !Can("ClientCabinet"))
            {
                return;
            }

            var clientId = await _db.Users
                .Where(x => x.Id == userId)
                .Select(x => x.ClientId)
                .FirstOrDefaultAsync(HttpContext.RequestAborted);
            HasClientAccount = clientId != null;

            AvailableMachineCount = clientId == null
                ? 0
                : await _db.ClientMachines.CountAsync(x => x.ClientId == clientId, HttpContext.RequestAborted);

            CompanyUserCount = clientId == null || !Can("CompanyUsers")
                ? 0
                : await _db.Users.CountAsync(x => x.ClientId == clientId, HttpContext.RequestAborted);

            ClientOpenTicketCount = await _db.Tickets.CountAsync(x => x.ClientUserId == userId && x.Status != TicketStatuses.Closed, HttpContext.RequestAborted);

            UnreadMessageCount = await _db.ChatMessages
                .Include(x => x.Ticket)
                .CountAsync(x =>
                    x.Ticket!.ClientUserId == userId &&
                    x.AuthorUserId != userId &&
                    !x.IsReadByClient,
                    HttpContext.RequestAborted);

            RecentClientTickets = await _db.Tickets
                .Include(x => x.Machine)
                .Where(x => x.ClientUserId == userId)
                .OrderByDescending(x => x.CreatedAt)
                .Take(5)
                .ToListAsync(HttpContext.RequestAborted);

            ClientTicketsWithUnreadMessages = await _db.ChatMessages
                .Include(x => x.Ticket)
                .Where(x =>
                    x.Ticket!.ClientUserId == userId &&
                    x.AuthorUserId != userId &&
                    !x.IsReadByClient)
                .Select(x => x.TicketId)
                .ToHashSetAsync(HttpContext.RequestAborted);
        }

        private async Task LoadOperatorMetricsAsync(string? userId)
        {
            if (string.IsNullOrWhiteSpace(userId) || !Can("OperatorQueue"))
            {
                return;
            }

            AssignedTickets = await _db.Tickets
                .Include(x => x.Machine)
                .Include(x => x.ClientUser)
                .Include(x => x.OperatorAssignments)
                .Include(x => x.Messages)
                .Where(x => x.Status != TicketStatuses.Closed)
                .Where(x => x.OperatorUserId == userId || x.OperatorAssignments.Any(a => a.OperatorUserId == userId))
                .OrderByDescending(x => x.Messages.Any(m => m.AuthorUserId != userId && !m.IsReadByOperator))
                .ThenByDescending(x => x.CreatedAt)
                .Take(6)
                .ToListAsync(HttpContext.RequestAborted);

            AssignedTicketCount = AssignedTickets.Count;
            ShowOperatorArea = ShowOperatorArea || AssignedTicketCount > 0;
        }
    }
}
