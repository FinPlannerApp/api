using System.Security.Claims;
using Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace API.Middleware;

/// <summary>
/// Rejects an access token whose session has been signed out.
///
/// Several devices can be signed in at once. A session is live while at least one of its refresh
/// tokens is unrevoked and unexpired; logging out, "sign out this device", "sign out other
/// devices", a password reset or detected token reuse all revoke those tokens, so the access token
/// stops working on its very next request instead of lingering for the rest of its 15 minutes.
///
/// IP and User-Agent are intentionally not checked: mobile users legitimately change IP when
/// switching WiFi and 4G, and a browser update changes the User-Agent.
/// </summary>
public class SessionValidationMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<SessionValidationMiddleware> _logger;

    public SessionValidationMiddleware(RequestDelegate next, ILogger<SessionValidationMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, UserManager<ApplicationUser> userManager)
    {
        if (context.User.Identity?.IsAuthenticated == true)
        {
            var userId    = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var sessionId = context.User.FindFirst("SessionId")?.Value;

            if (!string.IsNullOrEmpty(userId) && !string.IsNullOrEmpty(sessionId))
            {
                var now = DateTime.UtcNow;
                var sessionIsLive = await userManager.Users
                    .Where(u => u.Id == userId)
                    .SelectMany(u => u.RefreshTokens)
                    .AnyAsync(t => t.SessionId == sessionId && t.RevokedUtc == null && t.ExpiresUtc > now);

                if (!sessionIsLive)
                {
                    _logger.LogWarning("Rejected a token for user {UserId}: session {SessionId} is signed out.", userId, sessionId);

                    context.Response.StatusCode  = StatusCodes.Status401Unauthorized;
                    context.Response.ContentType = "application/json";
                    await context.Response.WriteAsJsonAsync(new
                    {
                        Message = "Session expired or signed out."
                    });
                    return;
                }
            }
        }

        await _next(context);
    }
}
