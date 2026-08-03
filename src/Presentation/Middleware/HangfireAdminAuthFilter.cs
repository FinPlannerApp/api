using Hangfire.Dashboard;
using Microsoft.AspNetCore.Identity;

namespace API.Middleware;

/// <summary>
/// Hangfire dashboard authorization filter.
/// Allows full access in local development environment.
/// In production, requires the user to be authenticated and have the 'Admin' role.
/// </summary>
public class HangfireAdminAuthFilter : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context)
    {
        var httpContext = context.GetHttpContext();
        var env = httpContext.RequestServices.GetRequiredService<IWebHostEnvironment>();

        if (env.IsDevelopment())
        {
            return true;
        }

        return httpContext.User.Identity?.IsAuthenticated == true && httpContext.User.IsInRole("Admin");
    }
}
