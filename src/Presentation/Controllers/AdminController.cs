using Application.Services;
using Domain.Entities;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;

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

    // ── Shared guards ────────────────────────────────────────────────────────
    // Constant-time comparison so the secret can't be recovered through response timing.
    private static bool SecretMatches(string? expected, string? provided)
    {
        if (string.IsNullOrWhiteSpace(expected) || provided is null) return false;
        var a = SHA256.HashData(Encoding.UTF8.GetBytes(expected));
        var b = SHA256.HashData(Encoding.UTF8.GetBytes(provided));
        return CryptographicOperations.FixedTimeEquals(a, b);
    }

    // Maintenance endpoints (diagnose / repair / migrate) are OFF unless explicitly enabled
    // in configuration for the duration of a deployment task.
    private static bool MaintenanceEnabled(IConfiguration config)
        => config.GetValue<bool>("Admin:EnableMaintenanceEndpoints");

    /// <summary>
    /// How many Redis (Upstash) commands this app has issued today, by category, with a monthly
    /// projection and whether optional Redis use has been paused to protect the quota.
    /// </summary>
    [HttpGet("redis-usage")]
    [Authorize(Roles = "Admin")]
    public IActionResult RedisUsage([FromServices] Infrastructure.Services.RedisQuotaGuard guard)
        => Ok(guard.Snapshot());

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
    /// Bootstrap the FIRST admin. Sign in first, then call with your own email and the
    /// AdminBootstrapKey from configuration. Works only while no admin exists.
    /// POST /api/admin/bootstrap { "email": "you@example.com", "secretKey": "your-secret" }
    /// </summary>
    [HttpPost("bootstrap")]
    public async Task<ActionResult> BootstrapAdmin([FromBody] BootstrapAdminDto input, [FromServices] IConfiguration config)
    {
        var expectedKey = config["AdminBootstrapKey"];
        if (string.IsNullOrWhiteSpace(expectedKey))
            return NotFound(); // feature not configured: behave as if it does not exist

        if (!SecretMatches(expectedKey, input.SecretKey))
            return Unauthorized(new { message = "Invalid secret key." });

        // Must be signed in, and can only elevate yourself — a leaked key alone can't
        // promote an arbitrary account.
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Unauthorized();
        if (!string.Equals(user.Email, input.Email, StringComparison.OrdinalIgnoreCase))
            return Forbid();

        // One-time: once any admin exists, further admins go through /promote.
        if ((await _userManager.GetUsersInRoleAsync("Admin")).Any())
            return Conflict(new { message = "An admin already exists. Ask them to promote you." });

        if (!await _roleManager.RoleExistsAsync("Admin"))
            await _roleManager.CreateAsync(new IdentityRole("Admin"));

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
    /// Get database diagnostic info including applied/pending migrations, schemas, and tables.
    /// Protected by the bootstrap secret key.
    /// POST /api/admin/diagnose { "secretKey": "your-secret" }
    /// </summary>
    [HttpPost("diagnose")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult> DiagnoseDb(
        [FromBody] MigrateDto input,
        [FromServices] IConfiguration config,
        [FromServices] ApplicationDbContext db,
        [FromServices] ILogger<AdminController> logger)
    {
        if (!MaintenanceEnabled(config)) return NotFound();
        if (!SecretMatches(config["AdminBootstrapKey"], input.SecretKey))
            return Unauthorized(new { message = "Invalid secret key." });

        try
        {
            var pending = (await db.Database.GetPendingMigrationsAsync()).ToList();
            var applied = (await db.Database.GetAppliedMigrationsAsync()).ToList();

            var schemas = new List<string>();
            var publicTables = new List<string>();
            var issueTables = new List<string>();

            var conn = db.Database.GetDbConnection();
            var wasClosed = conn.State == System.Data.ConnectionState.Closed;
            if (wasClosed) await conn.OpenAsync();

            try
            {
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT schema_name FROM information_schema.schemata ORDER BY schema_name;";
                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            schemas.Add(reader.GetString(0));
                        }
                    }
                }

                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT table_name FROM information_schema.tables WHERE table_schema = 'public' ORDER BY table_name;";
                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            publicTables.Add(reader.GetString(0));
                        }
                    }
                }

                if (schemas.Contains("issue"))
                {
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "SELECT table_name FROM information_schema.tables WHERE table_schema = 'issue' ORDER BY table_name;";
                        using (var reader = await cmd.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                issueTables.Add(reader.GetString(0));
                            }
                        }
                    }
                }
            }
            finally
            {
                if (wasClosed) await conn.CloseAsync();
            }

            return Ok(new
            {
                appliedMigrationsCount = applied.Count,
                appliedMigrations = applied,
                pendingMigrationsCount = pending.Count,
                pendingMigrations = pending,
                schemas,
                publicTablesCount = publicTables.Count,
                publicTables,
                issueTablesCount = issueTables.Count,
                issueTables
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Admin diagnostics failed.");
            return StatusCode(500, new { message = "Diagnostics failed. See server logs." });
        }
    }

    /// <summary>
    /// Repair database migration state by cleaning up v5.0.0 migration history records 
    /// that were imported but not actually created, and then re-running migrations.
    /// POST /api/admin/repair-db { "secretKey": "your-secret" }
    /// </summary>
    [HttpPost("repair-db")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult> RepairDb(
        [FromBody] MigrateDto input,
        [FromServices] IConfiguration config,
        [FromServices] ApplicationDbContext db,
        [FromServices] TaxonomySeederService seeder,
        [FromServices] ILogger<AdminController> logger)
    {
        if (!MaintenanceEnabled(config)) return NotFound();
        if (!SecretMatches(config["AdminBootstrapKey"], input.SecretKey))
            return Unauthorized(new { message = "Invalid secret key." });

        var results = new List<string>();

        try
        {
            var conn = db.Database.GetDbConnection();
            var wasClosed = conn.State == System.Data.ConnectionState.Closed;
            if (wasClosed) await conn.OpenAsync();

            int rowsDeleted = 0;
            try
            {
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"
                        -- 1. Create public history table
                        CREATE TABLE IF NOT EXISTS public.""__EFMigrationsHistory"" (
                            ""MigrationId"" character varying(150) NOT NULL,
                            ""ProductVersion"" character varying(32) NOT NULL,
                            CONSTRAINT ""PK___EFMigrationsHistory"" PRIMARY KEY (""MigrationId"")
                        );

                        -- 2. Copy the 12 older applied migrations from identity to public history table
                        INSERT INTO public.""__EFMigrationsHistory"" (""MigrationId"", ""ProductVersion"")
                        SELECT ""MigrationId"", ""ProductVersion""
                        FROM identity.""__EFMigrationsHistory""
                        WHERE ""MigrationId"" NOT IN (
                            '20260515074348_AddUserIdToTransaction',
                            '20260519074920_AddIssueTrackingSystem',
                            '20260519080424_AddIssueVotes',
                            '20260519095253_AddCommentVotesAndReplies',
                            '20260519102104_AddIssueTypeField',
                            '20260519103216_AddPhase2Features',
                            '20260519111416_AddPhase3Infrastructure',
                            '20260522045614_AddIssueStatusHistory',
                            '20260522123237_Phase2OperationalIntelligence'
                        )
                        ON CONFLICT (""MigrationId"") DO NOTHING;

                        -- 3. Delete any of the 9 new migration records from public history table
                        DELETE FROM public.""__EFMigrationsHistory"" 
                        WHERE ""MigrationId"" IN (
                            '20260515074348_AddUserIdToTransaction',
                            '20260519074920_AddIssueTrackingSystem',
                            '20260519080424_AddIssueVotes',
                            '20260519095253_AddCommentVotesAndReplies',
                            '20260519102104_AddIssueTypeField',
                            '20260519103216_AddPhase2Features',
                            '20260519111416_AddPhase3Infrastructure',
                            '20260522045614_AddIssueStatusHistory',
                            '20260522123237_Phase2OperationalIntelligence'
                        );

                        -- 4. Delete the 9 new migration records from identity history table (cleanup)
                        DELETE FROM identity.""__EFMigrationsHistory"" 
                        WHERE ""MigrationId"" IN (
                            '20260515074348_AddUserIdToTransaction',
                            '20260519074920_AddIssueTrackingSystem',
                            '20260519080424_AddIssueVotes',
                            '20260519095253_AddCommentVotesAndReplies',
                            '20260519102104_AddIssueTypeField',
                            '20260519103216_AddPhase2Features',
                            '20260519111416_AddPhase3Infrastructure',
                            '20260522045614_AddIssueStatusHistory',
                            '20260522123237_Phase2OperationalIntelligence'
                        );";
                    rowsDeleted = await cmd.ExecuteNonQueryAsync();
                    results.Add("Successfully migrated applied migration history records from identity schema to public schema, and deleted v5 pending migrations from history.");
                }
            }
            finally
            {
                if (wasClosed) await conn.CloseAsync();
            }

            // 2. Re-apply migrations
            var pendingMigrations = (await db.Database.GetPendingMigrationsAsync()).ToList();
            if (pendingMigrations.Count > 0)
            {
                await db.Database.MigrateAsync();
                results.Add($"Successfully applied {pendingMigrations.Count} pending migration(s): {string.Join(", ", pendingMigrations)}");
            }
            else
            {
                results.Add("No pending migrations found after cleanup.");
            }

            // 3. Seed database
            await seeder.SeedAsync();
            results.Add("Seeding completed successfully.");

            return Ok(new { success = true, results });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Admin repair-db failed.");
            return StatusCode(500, new { message = "Repair failed. See server logs.", results });
        }
    }

    /// <summary>
    /// Apply pending EF Core migrations and seed taxonomy/roadmap data.
    /// Protected by the bootstrap secret key — call after deploy to set up production DB.
    /// POST /api/admin/migrate-and-seed { "secretKey": "your-secret", "bypassMigration": true }
    /// </summary>
    [HttpPost("migrate-and-seed")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult> MigrateAndSeed(
        [FromBody] MigrateDto input,
        [FromServices] IConfiguration config,
        [FromServices] ApplicationDbContext db,
        [FromServices] TaxonomySeederService seeder,
        [FromServices] ILogger<AdminController> logger)
    {
        if (!MaintenanceEnabled(config)) return NotFound();
        if (!SecretMatches(config["AdminBootstrapKey"], input.SecretKey))
            return Unauthorized(new { message = "Invalid secret key." });

        var results = new List<string>();

        try
        {
            // 1. Apply pending migrations (unless explicitly bypassed)
            if (input.BypassMigration != true)
            {
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
            }
            else
            {
                results.Add("Bypassed EF Core DDL migration because bypassMigration was set to true.");
            }

            // 2. Seed taxonomy categories and roadmap items
            await seeder.SeedAsync();
            results.Add("Taxonomy and roadmap seeding completed (skipped if data already exists).");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Admin migrate-and-seed failed.");
            return StatusCode(500, new { message = "Migration/seed failed. See server logs." });
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
    public bool? BypassMigration { get; set; }
}
