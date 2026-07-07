using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace TechSupportRagBot.Hubs;

[Authorize]
public class ChatHub : Hub
{
    public Task JoinTicket(int ticketId)
    {
        return Groups.AddToGroupAsync(Context.ConnectionId, TicketGroup(ticketId));
    }

    public Task LeaveTicket(int ticketId)
    {
        return Groups.RemoveFromGroupAsync(Context.ConnectionId, TicketGroup(ticketId));
    }

    public Task Typing(int ticketId, string displayName)
    {
        return Clients.OthersInGroup(TicketGroup(ticketId)).SendAsync("UserTyping", new
        {
            ticketId,
            displayName
        });
    }

    public static string TicketGroup(int ticketId) => $"ticket:{ticketId}";
}
