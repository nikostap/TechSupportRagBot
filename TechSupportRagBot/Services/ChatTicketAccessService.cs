using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TechSupportRagBot.Data;
using TechSupportRagBot.Models;

namespace TechSupportRagBot.Services;

public sealed class ChatTicketAccessService
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly AccessProfileService _accessProfiles;

    public ChatTicketAccessService(
        ApplicationDbContext db,
        UserManager<ApplicationUser> userManager,
        AccessProfileService accessProfiles)
    {
        _db = db;
        _userManager = userManager;
        _accessProfiles = accessProfiles;
    }

    public async Task<ChatTicketAccess?> AuthorizeAsync(
        ClaimsPrincipal principal,
        int ticketId,
        CancellationToken cancellationToken = default)
    {
        if (ticketId <= 0
            || principal.Identity?.IsAuthenticated != true
            || !await _accessProfiles.IsAllowedAsync(principal, "ChatWrite", cancellationToken))
        {
            return null;
        }

        var user = await _userManager.GetUserAsync(principal);
        if (user == null)
        {
            return null;
        }

        var roles = await _userManager.GetRolesAsync(user);
        var isInternalUser = roles.Contains("Admin") || roles.Contains("Operator");
        var hasTicketAccess = isInternalUser
            ? await _db.Tickets.AnyAsync(x => x.Id == ticketId, cancellationToken)
            : user.ClientId.HasValue && await _db.Tickets.AnyAsync(
                x => x.Id == ticketId
                    && _db.Users.Any(owner => owner.Id == x.ClientUserId && owner.ClientId == user.ClientId),
                cancellationToken);

        if (!hasTicketAccess)
        {
            return null;
        }

        var displayName = string.IsNullOrWhiteSpace(user.FullName)
            ? user.UserName ?? "User"
            : user.FullName;
        return new ChatTicketAccess(ticketId, displayName);
    }
}

public sealed record ChatTicketAccess(int TicketId, string DisplayName);
