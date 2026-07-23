using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using TechSupportRagBot.Services;

namespace TechSupportRagBot.Hubs;

[Authorize]
public class ChatHub : Hub
{
    private readonly ChatTicketAccessService _ticketAccess;

    public ChatHub(ChatTicketAccessService ticketAccess)
    {
        _ticketAccess = ticketAccess;
    }

    public async Task JoinTicket(int ticketId)
    {
        var access = await RequireAccessAsync(ticketId);
        await Groups.AddToGroupAsync(Context.ConnectionId, TicketGroup(access.TicketId));
    }

    public async Task LeaveTicket(int ticketId)
    {
        var access = await RequireAccessAsync(ticketId);
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, TicketGroup(access.TicketId));
    }

    public async Task Typing(int ticketId)
    {
        var access = await RequireAccessAsync(ticketId);
        await Clients.OthersInGroup(TicketGroup(access.TicketId)).SendAsync("UserTyping", new
        {
            ticketId = access.TicketId,
            access.DisplayName
        });
    }

    public static string TicketGroup(int ticketId) => $"ticket:{ticketId}";

    private async Task<ChatTicketAccess> RequireAccessAsync(int ticketId)
    {
        var access = await _ticketAccess.AuthorizeAsync(
            Context.User ?? new System.Security.Claims.ClaimsPrincipal(),
            ticketId,
            Context.ConnectionAborted);
        return access ?? throw new HubException("Access to the ticket is denied.");
    }
}
