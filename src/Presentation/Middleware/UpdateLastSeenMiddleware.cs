using Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace API.Middleware;

/// <summary>
/// Keeps ApplicationUser.LastSeenUtc roughly current (at most one write per 5 minutes per user).
/// A single conditional UPDATE: it no longer loads the whole user (with every refresh token and
/// device) on every request, and it bypasses the user row's concurrency stamp, so it can no longer
/// make a concurrent login/refresh fail with a concurrency conflict.
/// </summary>
public class UpdateLastSeenMiddleware
{
    private readonly RequestDelegate _next;

    public UpdateLastSeenMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, UserManager<ApplicationUser> userManager)
    {
        await _next(context);

        if (context.User.Identity?.IsAuthenticated != true) return;
        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId == null) return;

        try
        {
            var now = DateTime.UtcNow;
            var stale = now.AddMinutes(-5);
            await userManager.Users
                .Where(u => u.Id == userId && u.LastSeenUtc < stale)
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.LastSeenUtc, now));
        }
        catch
        {
            // Never fail a request over a bookkeeping update.
        }
    }
}
