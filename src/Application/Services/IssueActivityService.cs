using Application.Contracts;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace Application.Services;

/// <summary>
/// Service to log and fetch issue activities for audit feed tracking.
/// </summary>
public class IssueActivityService
{
    private readonly IApplicationDbContext _context;

    public IssueActivityService(IApplicationDbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Logs an activity event on an issue.
    /// </summary>
    public async Task LogActivityAsync(int issueId, string userId, string activityType, string description, object? metadata = null)
    {
        var metadataJson = metadata != null ? JsonSerializer.Serialize(metadata) : null;
        
        var activity = new IssueActivity
        {
            IssueId = issueId,
            UserId = userId,
            ActivityType = activityType,
            Description = description,
            Metadata = metadataJson
        };

        _context.IssueActivities.Add(activity);
        await _context.SaveChangesAsync();
    }

    /// <summary>
    /// Gets all activity feed entries for an issue, ordered by newest first.
    /// </summary>
    public async Task<List<IssueActivity>> GetActivitiesAsync(int issueId)
    {
        return await _context.IssueActivities
            .Where(a => a.IssueId == issueId)
            .OrderByDescending(a => a.CreatedAt)
            .ToListAsync();
    }
}
