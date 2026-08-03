using System.Security.Claims;
using Domain.Entities;
using Microsoft.AspNetCore.Identity;

namespace API.Middleware;

/// <summary>
/// Validates that the session ID embedded in the JWT still matches the user's
/// active session in the database (single-session enforcement).
///
/// INTENTIONALLY removed IP and User-Agent checks — mobile users legitimately
/// change IP when switching WiFi ↔ 4G. Those checks caused valid users to get
/// logged out randomly. The SessionId check alone prevents session fixation
/// and concurrent-session abuse without breaking real users.
/// Suspicious IP changes are now LOGGED only (audit trail without blocking).
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
                var user = await userManager.FindByIdAsync(userId);
                if (user != null)
                {
                    // ── Single-session enforcement ────────────────────────────────────
                    // If the session ID in the JWT doesn't match what's stored, the user
                    // has logged in elsewhere (or was force-logged-out). Reject this token.
                    if (user.CurrentSessionId != sessionId)
                    {
                        _logger.LogWarning(
                            "Session mismatch for user {UserId}. Token session={TokenSession}, DB session={DbSession}. Rejecting.",
                            userId, sessionId, user.CurrentSessionId ?? "null");

                        context.Response.StatusCode  = StatusCodes.Status401Unauthorized;
                        context.Response.ContentType = "application/json";
                        await context.Response.WriteAsJsonAsync(new
                        {
                            Message = "Session expired or signed in from another device."
                        });
                        return;
                    }

                    // ── Audit-only: log IP change (no block) ──────────────────────────
                    // Blocking on IP change breaks mobile users (WiFi ↔ 4G switching).
                    // We log it for security audit trail instead.
                    var currentIp = context.Request.Headers.TryGetValue("X-Forwarded-For", out var fwd)
                        ? fwd.ToString().Split(',')[0].Trim()
                        : context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

                    if (!string.IsNullOrEmpty(user.LastKnownIp) && user.LastKnownIp != currentIp)
                    {
                        _logger.LogInformation(
                            "IP change detected for user {UserId}: {OldIp} → {NewIp} (allowed, audit only)",
                            userId, user.LastKnownIp, currentIp);
                    }
                }
            }
        }

        await _next(context);
    }
}
