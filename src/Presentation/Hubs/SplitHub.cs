using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace API.Hubs;

[Authorize]
public class SplitHub : Hub
{
    public async Task JoinGroup(int groupId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, GetGroupName(groupId));
    }

    public async Task LeaveGroup(int groupId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, GetGroupName(groupId));
    }

    public static string GetGroupName(int groupId) => $"split-group-{groupId}";
}
