using API.Extensions;
using API.Middleware;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.CookiePolicy;
using Serilog;
using Serilog.Formatting.Compact;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, lc) => 
{
    lc.ReadFrom.Configuration(ctx.Configuration)
      .Enrich.FromLogContext();

    if (ctx.HostingEnvironment.IsDevelopment())
    {
        lc.WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}");
    }
    else
    {
        lc.WriteTo.Console(new CompactJsonFormatter());
    }

    lc.WriteTo.File("logs/app-.txt",
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 30);
});

// --- Dependency Injection Setup ---
// This handles all service registrations (Application, Infrastructure, API)
builder.Services.AddPresentationLayerServices(builder.Configuration);

// Add Memory Cache for Custom Rate Limiting
builder.Services.AddMemoryCache();

// Configure for Render.com reverse proxy (Cloudflare CDN + Render proxy)
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
    // Trust all proxies - required for cloud platforms like Render.com
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
    options.ForwardLimit = 2; // Limit to 2 proxy hops (Cloudflare + Render)
});

var app = builder.Build();

app.UseSerilogRequestLogging();

// --- Middleware Pipeline ---
// 1. Forwarded Headers (Must be first to identify client IP)
app.UseForwardedHeaders();

// 2. Global Exception Handler
app.UseMiddleware<ExceptionHandlingMiddleware>();

// 2.5 Response Compression
app.UseResponseCompression();

// 3. Health Checks (Liveness/Readiness)
app.MapGet("/healthz", () => Results.Ok("ok"));
app.MapGet("/health/db", async (ApplicationDbContext db) =>
{
    return await db.Database.CanConnectAsync()
        ? Results.Ok("db-ok")
        : Results.Problem("db-fail", statusCode: 503);
});

// CSRF Token Endpoint (for Angular to retrieve anti-forgery token)
app.MapGet("/api/antiforgery/token", (HttpContext context) =>
{
    var antiforgery = context.RequestServices.GetRequiredService<Microsoft.AspNetCore.Antiforgery.IAntiforgery>();
    var tokens = antiforgery.GetAndStoreTokens(context);
    context.Response.Cookies.Append("XSRF-TOKEN", tokens.RequestToken!, new CookieOptions 
    { 
        HttpOnly = false, // Angular needs to read this
        Secure = context.Request.IsHttps,
        SameSite = SameSiteMode.Strict
    });
    return Results.Ok(new { token = tokens.RequestToken });
}).RequireCors("AllowSpecificOrigins");

// 4. Swagger
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
else
{
    app.UseHsts();
}

if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

// Serve static files from wwwroot (e.g. uploaded issue attachments)
app.UseStaticFiles();

app.UseCookiePolicy(new CookiePolicyOptions
{
    MinimumSameSitePolicy = SameSiteMode.Strict,
    HttpOnly = HttpOnlyPolicy.Always,
    Secure = CookieSecurePolicy.Always
});

app.UseRateLimiter();

// 5. CORS
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    try
    {
        var db = services.GetRequiredService<ApplicationDbContext>();
        // db.Database.Migrate(); // Disabled for least-privilege user support
        var seeder = services.GetRequiredService<Application.Services.TaxonomySeederService>();
        await seeder.SeedAsync();

        var challengeSeeder = services.GetRequiredService<Application.Services.ChallengeSeederService>();
        await challengeSeeder.SeedAsync();

        // Seed Admin role
        var roleManager = services.GetRequiredService<Microsoft.AspNetCore.Identity.RoleManager<Microsoft.AspNetCore.Identity.IdentityRole>>();
        if (!await roleManager.RoleExistsAsync("Admin"))
        {
            await roleManager.CreateAsync(new Microsoft.AspNetCore.Identity.IdentityRole("Admin"));
        }
    }
    catch (Exception ex)
    {
        var logger = services.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "An error occurred while seeding the database.");
    }
}
app.UseCors("AllowSpecificOrigins");

// Security Headers
app.Use(async (context, next) =>
{
    context.Response.Headers.Append("X-Content-Type-Options", "nosniff");
    context.Response.Headers.Append("X-Frame-Options", "DENY");
    context.Response.Headers.Append("Referrer-Policy", "strict-origin-when-cross-origin");

    // Dynamically allow the request's origin and host in connect-src (crucial for local network/IP hosting)
    var origin = context.Request.Headers.Origin.ToString();
    var host = context.Request.Host.Value;
    var dynamicConnectSrc = "";
    if (!string.IsNullOrEmpty(origin))
    {
        dynamicConnectSrc += $" {origin}";
    }
    if (!string.IsNullOrEmpty(host))
    {
        dynamicConnectSrc += $" http://{host} https://{host}";
    }

    context.Response.Headers.Append("Content-Security-Policy", 
        $"default-src 'self' https://finplanner.ska97homelab.uk; " +
        $"script-src 'self' 'unsafe-inline' 'unsafe-eval' https://www.googletagmanager.com https://static.cloudflareinsights.com https://finplanner.ska97homelab.uk; " +
        $"style-src 'self' 'unsafe-inline' https://fonts.googleapis.com https://finplanner.ska97homelab.uk; " +
        $"img-src 'self' data: https:; " +
        $"font-src 'self' data: https://fonts.gstatic.com; " +
        $"connect-src 'self' https: http://localhost:5018 https://localhost:7123{dynamicConnectSrc} https://www.google-analytics.com https://www.googletagmanager.com https://cloudflareinsights.com https://finplanner.ska97homelab.uk; " +
        $"object-src 'none'; base-uri 'self';");

    await next();
});

app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<SessionValidationMiddleware>();
app.UseMiddleware<UpdateLastSeenMiddleware>();

app.MapControllers();
app.MapHub<API.Hubs.SplitHub>("/hubs/split");

app.Run();