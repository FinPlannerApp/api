using Application.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Infrastructure.BackgroundJobs;

/// <summary>
/// Hourly Hangfire background job that recalculates PainVelocity for all open issues.
/// </summary>
public class UpdatePainVelocityJob
{
    private readonly IApplicationDbContext _context;
    private readonly ILogger<UpdatePainVelocityJob> _logger;

    public UpdatePainVelocityJob(IApplicationDbContext context, ILogger<UpdatePainVelocityJob> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <summary>
    /// Recalculates PainVelocity = PainScore / AgeInDays (minimum 1.0 day divisor).
    /// </summary>
    public async Task UpdateVelocitiesAsync()
    {
        _logger.LogInformation("Hangfire Job: Updating Issue Pain Velocities...");

        var openIssues = await _context.Issues
            .Where(i => !i.IsClosed)
            .ToListAsync();

        if (openIssues.Count == 0)
        {
            _logger.LogInformation("No open issues to update.");
            return;
        }

        var now = DateTime.UtcNow;
        foreach (var issue in openIssues)
        {
            var ageInDays = (now - issue.CreatedAt).TotalDays;
            var divisor = Math.Max(1.0, ageInDays);
            issue.PainVelocity = issue.PainScore / divisor;
        }

        await _context.SaveChangesAsync();
        _logger.LogInformation("Successfully updated pain velocities for {Count} issues.", openIssues.Count);
    }
}
