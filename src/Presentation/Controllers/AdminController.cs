using Application.Services;
using Domain.Entities;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Presentation.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class AdminController : ControllerBase
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<IdentityRole> _roleManager;

    public AdminController(UserManager<ApplicationUser> userManager, RoleManager<IdentityRole> roleManager)
    {
        _userManager = userManager;
        _roleManager = roleManager;
    }

    /// <summary>
    /// Promote a user to Admin role. Only existing admins can do this.
    /// For the FIRST admin, use: POST /api/admin/bootstrap with the secret key.
    /// </summary>
    [HttpPost("promote")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult> PromoteToAdmin([FromBody] PromoteDto input)
    {
        var user = await _userManager.FindByEmailAsync(input.Email);
        if (user == null) return NotFound(new { message = "User not found." });

        if (await _userManager.IsInRoleAsync(user, "Admin"))
            return Ok(new { message = "User is already an Admin." });

        await _userManager.AddToRoleAsync(user, "Admin");
        return Ok(new { message = $"{user.Name} has been promoted to Admin." });
    }

    /// <summary>
    /// Bootstrap the FIRST admin. Uses a secret key from appsettings.
    /// Call this once to make yourself admin, then use /promote for others.
    /// POST /api/admin/bootstrap { "email": "you@example.com", "secretKey": "your-secret" }
    /// </summary>
    [HttpPost("bootstrap")]
    [AllowAnonymous]
    public async Task<ActionResult> BootstrapAdmin([FromBody] BootstrapAdminDto input, [FromServices] IConfiguration config)
    {
        // Use a secret key from configuration (or fallback for dev)
        var expectedKey = config["AdminBootstrapKey"] ?? "admin-setup-key-2026";
        
        if (input.SecretKey != expectedKey)
            return Unauthorized(new { message = "Invalid secret key." });

        var user = await _userManager.FindByEmailAsync(input.Email);
        if (user == null) return NotFound(new { message = "User not found. Register first." });

        // Ensure role exists
        if (!await _roleManager.RoleExistsAsync("Admin"))
            await _roleManager.CreateAsync(new IdentityRole("Admin"));

        if (await _userManager.IsInRoleAsync(user, "Admin"))
            return Ok(new { message = "User is already an Admin." });

        await _userManager.AddToRoleAsync(user, "Admin");
        return Ok(new { message = $"{user.Name} is now an Admin. Log out and log back in to activate." });
    }

    [HttpGet("check")]
    public async Task<ActionResult> CheckAdminStatus()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Unauthorized();

        var isAdmin = await _userManager.IsInRoleAsync(user, "Admin");
        return Ok(new { isAdmin, userName = user.UserName, email = user.Email });
    }

    /// <summary>
    /// Apply pending EF Core migrations and seed taxonomy/roadmap data.
    /// Protected by the bootstrap secret key — call after deploy to set up production DB.
    /// POST /api/admin/migrate-and-seed { "secretKey": "your-secret" }
    /// </summary>
    [HttpPost("migrate-and-seed")]
    [AllowAnonymous]
    public async Task<ActionResult> MigrateAndSeed(
        [FromBody] MigrateDto input,
        [FromServices] IConfiguration config,
        [FromServices] ApplicationDbContext db,
        [FromServices] TaxonomySeederService seeder)
    {
        var expectedKey = config["AdminBootstrapKey"] ?? "admin-setup-key-2026";
        if (input.SecretKey != expectedKey)
            return Unauthorized(new { message = "Invalid secret key." });

        var results = new List<string>();

        try
        {
            // 1. Apply pending migrations
            var pendingMigrations = (await db.Database.GetPendingMigrationsAsync()).ToList();
            if (pendingMigrations.Count > 0)
            {
                await db.Database.MigrateAsync();
                results.Add($"Applied {pendingMigrations.Count} migration(s): {string.Join(", ", pendingMigrations)}");
            }
            else
            {
                results.Add("No pending migrations.");
            }

            // 2. Seed taxonomy categories and roadmap items
            await seeder.SeedAsync();
            results.Add("Taxonomy and roadmap seeding completed (skipped if data already exists).");
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "Migration/seed failed.", error = ex.Message, innerError = ex.InnerException?.Message });
        }

        return Ok(new { success = true, results });
    }
}

public class PromoteDto
{
    public required string Email { get; set; }
}

public class BootstrapAdminDto
{
    public required string Email { get; set; }
    public required string SecretKey { get; set; }
}

public class MigrateDto
{
    public required string SecretKey { get; set; }
}
