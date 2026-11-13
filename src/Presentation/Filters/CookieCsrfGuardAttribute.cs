using API.Extensions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace API.Filters;

/// <summary>
/// CSRF defence for endpoints that act on the refresh-token cookie (refresh, logout).
/// The cookie is SameSite=None so a foreign site's page could make the browser send it.
/// Two checks stop that without a token round-trip:
///  1. A browser that sends an Origin header must send an allow-listed one.
///  2. The request must carry "X-Requested-With: XMLHttpRequest". A foreign page cannot add a custom
///     header to a credentialed request unless CORS allows its origin, and it does not.
/// Non-browser clients send no Origin and are not CSRF-able.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class CookieCsrfGuardAttribute : ActionFilterAttribute
{
    public override void OnActionExecuting(ActionExecutingContext context)
    {
        var http = context.HttpContext;
        var config = http.RequestServices.GetRequiredService<IConfiguration>();

        // Security:CsrfGuard:Mode = Off | ReportOnly (default) | Enforce
        // ReportOnly logs what WOULD be blocked but lets the request through, so the API can be
        // deployed before every client (cached PWA bundles, other front ends) sends the header.
        // Switch to Enforce once the logs show no more "would reject" lines.
        var mode = config["Security:CsrfGuard:Mode"] ?? "ReportOnly";
        if (string.Equals(mode, "Off", StringComparison.OrdinalIgnoreCase))
        {
            base.OnActionExecuting(context);
            return;
        }

        var allowed = CorsOrigins.Get(config);
        string? problem = null;

        var origin = http.Request.Headers.Origin.ToString();
        if (!string.IsNullOrEmpty(origin) && !allowed.Contains(origin.TrimEnd('/')))
            problem = $"Origin not allowed: {origin}";
        else if (!string.Equals(http.Request.Headers["X-Requested-With"].ToString(), "XMLHttpRequest", StringComparison.OrdinalIgnoreCase))
            problem = "Missing X-Requested-With header";

        if (problem != null)
        {
            var enforce = string.Equals(mode, "Enforce", StringComparison.OrdinalIgnoreCase);
            http.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("CookieCsrfGuard")
                .LogWarning("CSRF guard {Action} {Path}: {Problem} (mode {Mode})",
                    enforce ? "rejected" : "would reject", http.Request.Path, problem, mode);

            if (enforce)
            {
                context.Result = new ObjectResult(new { isSuccess = false, error = new { code = "Csrf.Rejected", description = "Request rejected." } })
                { StatusCode = StatusCodes.Status403Forbidden };
                return;
            }
        }

        base.OnActionExecuting(context);
    }
}
