using Application.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Infrastructure.BackgroundJobs;

/// <summary>
/// Hangfire daily job: purges expired and revoked refresh tokens from the DB.
///
/// WHY THIS EXISTS:
///   RefreshTokens are stored as owned entities on ApplicationUser.
///   LoginAsync already does an in-memory cleanup per user on each login,
///   but tokens for users who never log in again accumulate forever.
///   This job ensures the table stays bounded regardless of user activity.
///
/// SCHEDULE: Daily at 02:00 UTC (low-traffic window).
///
/// REGISTER IN Program.cs:
///   RecurringJob.AddOrUpdate{RefreshTokenCleanupJob}(
///       "cleanup-refresh-tokens",
///       job => job.CleanupAsync(),
///       "0 2 * * *");   // daily at 02:00 UTC
/// </summary>
public class RefreshTokenCleanupJob
{
    private readonly IApplicationDbContext _context;
    private readonly ILogger<RefreshTokenCleanupJob> _logger;

    // Keep tokens for this many days after expiry/revocation for audit trail.
    // Adjust down if DB storage is a concern.
    private const int RetentionDaysAfterExpiry = 30;

    public RefreshTokenCleanupJob(IApplicationDbContext context, ILogger<RefreshTokenCleanupJob> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task CleanupAsync()
    {
        _logger.LogInformation("Hangfire Job: Starting RefreshToken cleanup...");

        var cutoff = DateTime.UtcNow.AddDays(-RetentionDaysAfterExpiry);

        // Load users who have any old tokens (avoids loading all users)
        // RefreshToken is an owned entity — EF loads them as part of the user aggregate.
        var usersWithOldTokens = await _context.Users
            .Where(u => u.RefreshTokens.Any(rt =>
                (rt.ExpiresUtc < cutoff) ||           // expired > 30 days ago
                (rt.RevokedUtc != null && rt.RevokedUtc < cutoff)))  // revoked > 30 days ago
            .ToListAsync();

        if (!usersWithOldTokens.Any())
        {
            _logger.LogInformation("RefreshToken cleanup: nothing to clean.");
            return;
        }

        int totalRemoved = 0;

        foreach (var user in usersWithOldTokens)
        {
            var before = user.RefreshTokens.Count;

            user.RefreshTokens.RemoveAll(rt =>
                (rt.ExpiresUtc < cutoff) ||
                (rt.RevokedUtc != null && rt.RevokedUtc < cutoff));

            totalRemoved += before - user.RefreshTokens.Count;
        }

        await _context.SaveChangesAsync(default);

        _logger.LogInformation(
            "RefreshToken cleanup complete. Removed {Count} stale tokens from {Users} users.",
            totalRemoved, usersWithOldTokens.Count);
    }
}
