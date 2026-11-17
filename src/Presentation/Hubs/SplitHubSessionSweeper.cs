using Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace API.Hubs;

/// <summary>
/// SEC-24: a WebSocket authenticates once, when it connects, so signing a device out would otherwise
/// leave its live socket receiving group events until it dropped by itself. Every 30 seconds this checks
/// each open socket's login session and, for any that were signed out, removes the socket from all of
/// its group rooms and tells the client to close.
/// </summary>
public class SplitHubSessionSweeper : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);
    private readonly IServiceScopeFactory _scopes;
    private readonly IHubContext<SplitHub> _hub;
    private readonly ILogger<SplitHubSessionSweeper> _logger;

    public SplitHubSessionSweeper(IServiceScopeFactory scopes, IHubContext<SplitHub> hub, ILogger<SplitHubSessionSweeper> logger)
    {
        _scopes = scopes;
        _hub = hub;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try { await SweepAsync(stoppingToken); }
            catch (OperationCanceledException) { }
            catch (Exception ex) { _logger.LogError(ex, "SignalR session sweep failed."); }
        }
    }

    private async Task SweepAsync(CancellationToken ct)
    {
        var conns = SplitHub.Snapshot();
        if (conns.Count == 0) return;

        using var scope = _scopes.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var now = DateTime.UtcNow;

        // One query per distinct session, not per socket.
        foreach (var session in conns.GroupBy(c => (c.UserId, c.SessionId)))
        {
            var live = await users.Users
                .Where(u => u.Id == session.Key.UserId)
                .SelectMany(u => u.RefreshTokens)
                .AnyAsync(t => t.SessionId == session.Key.SessionId && t.RevokedUtc == null && t.ExpiresUtc > now, ct);
            if (live) continue;

            foreach (var c in session)
            {
                foreach (var gid in c.GroupIds)
                    await _hub.Groups.RemoveFromGroupAsync(c.ConnectionId, SplitHub.GetGroupName(gid), ct);
                SplitHub.Forget(c.ConnectionId);
                await _hub.Clients.Client(c.ConnectionId).SendAsync("SessionRevoked", ct);
                _logger.LogInformation("Cut SignalR socket {Conn} of user {User}: session signed out.", c.ConnectionId, c.UserId);
            }
        }
    }
}
