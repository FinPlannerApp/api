using System.Security.Claims;
using Application.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace API.Hubs;

[Authorize]
public class SplitHub : Hub
{
    private readonly IApplicationDbContext _context;

    public SplitHub(IApplicationDbContext context) => _context = context;

    // userId -> live connection ids (this process). Lets the server drop a removed member from a room immediately.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, System.Collections.Concurrent.ConcurrentDictionary<string, byte>> Live = new();

    // connectionId -> (userId, sessionId, joined group ids). Used to cut off sockets whose login session was revoked (SEC-24).
    private sealed class ConnInfo
    {
        public required string UserId { get; init; }
        public required string SessionId { get; init; }
        public System.Collections.Concurrent.ConcurrentDictionary<int, byte> Groups { get; } = new();
    }
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, ConnInfo> Conns = new();

    public static IReadOnlyCollection<string> ConnectionsOf(string userId)
        => Live.TryGetValue(userId, out var set) ? set.Keys.ToArray() : Array.Empty<string>();

    /// <summary>Snapshot for the session sweeper: (connectionId, userId, sessionId, groupIds).</summary>
    public static IReadOnlyList<(string ConnectionId, string UserId, string SessionId, int[] GroupIds)> Snapshot()
        => Conns.Select(kv => (kv.Key, kv.Value.UserId, kv.Value.SessionId, kv.Value.Groups.Keys.ToArray())).ToList();

    public static void Forget(string connectionId) => Conns.TryRemove(connectionId, out _);

    public override Task OnConnectedAsync()
    {
        var userId = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        var sessionId = Context.User?.FindFirstValue("SessionId");
        if (!string.IsNullOrEmpty(userId))
        {
            Live.GetOrAdd(userId, _ => new()).TryAdd(Context.ConnectionId, 0);
            if (!string.IsNullOrEmpty(sessionId))
                Conns[Context.ConnectionId] = new ConnInfo { UserId = userId, SessionId = sessionId };
        }
        return base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        Conns.TryRemove(Context.ConnectionId, out _);
        var userId = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!string.IsNullOrEmpty(userId) && Live.TryGetValue(userId, out var set))
        {
            set.TryRemove(Context.ConnectionId, out _);
            if (set.IsEmpty) Live.TryRemove(userId, out _);
        }
        return base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    /// Only the group's creator or an active member may subscribe to its live updates.
    /// Previously ANY signed-in user could join ANY group id and receive its expense events.
    /// </summary>
    public async Task JoinGroup(int groupId)
    {
        var userId = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
            throw new HubException("Not authorised.");

        var allowed = await _context.SplitGroups.AnyAsync(g =>
            g.Id == groupId &&
            (g.CreatedByUserId == userId ||
             g.Members.Any(m => m.LinkedUserId == userId && m.LeftAt == null)));

        // Same response whether the group doesn't exist or isn't yours — no probing of ids.
        if (!allowed)
            throw new HubException("Group not found.");

        await Groups.AddToGroupAsync(Context.ConnectionId, GetGroupName(groupId));
        if (Conns.TryGetValue(Context.ConnectionId, out var info)) info.Groups[groupId] = 0;
    }

    public async Task LeaveGroup(int groupId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, GetGroupName(groupId));
        if (Conns.TryGetValue(Context.ConnectionId, out var info)) info.Groups.TryRemove(groupId, out _);
    }

    public static string GetGroupName(int groupId) => $"split-group-{groupId}";
}
