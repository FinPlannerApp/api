using Application.Contracts;
using Application.DTOs.Auth;
using Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Application.Services;

/// <summary>
/// Centralized workflow engine that enforces valid issue status transitions,
/// updates SLA timestamps, logs history, and sends notifications.
/// </summary>
public class IssueWorkflowService
{
    private readonly IApplicationDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IEmailService _emailService;

    // Transition rules: which statuses can transition to which, and who is allowed
    private static readonly Dictionary<IssueStatus, List<(IssueStatus To, bool RequiresAdmin)>> TransitionMap = new()
    {
        [IssueStatus.New] = new()
        {
            (IssueStatus.Acknowledged, false),
            (IssueStatus.Triaged, false),
            (IssueStatus.Planned, true),       // Roadmap — admin only
            (IssueStatus.Closed, false)
        },
        [IssueStatus.Acknowledged] = new()
        {
            (IssueStatus.Triaged, false),
            (IssueStatus.Planned, true),
            (IssueStatus.Closed, false)
        },
        [IssueStatus.Triaged] = new()
        {
            (IssueStatus.Planned, true),
            (IssueStatus.Closed, false)
        },
        [IssueStatus.Planned] = new()
        {
            (IssueStatus.InProgress, true),
            (IssueStatus.Triaged, true),       // Demote
            (IssueStatus.Closed, true)
        },
        [IssueStatus.InProgress] = new()
        {
            (IssueStatus.Released, true),
            (IssueStatus.Planned, true),       // Rollback
            (IssueStatus.Closed, true)
        },
        [IssueStatus.Released] = new()
        {
            (IssueStatus.Verified, false),
            (IssueStatus.InProgress, true),    // Regression
            (IssueStatus.Closed, false)
        },
        [IssueStatus.Verified] = new()
        {
            (IssueStatus.Closed, false),
            (IssueStatus.Released, true)       // Reopen from verified
        },
        [IssueStatus.Closed] = new()
        {
            (IssueStatus.New, false)            // Reopen
        }
    };

    private readonly IssueActivityService _activityService;

    public IssueWorkflowService(
        IApplicationDbContext context,
        UserManager<ApplicationUser> userManager,
        IEmailService emailService,
        IssueActivityService activityService)
    {
        _context = context;
        _userManager = userManager;
        _emailService = emailService;
        _activityService = activityService;
    }

    /// <summary>
    /// Validate and execute a status transition.
    /// Returns (success, message, newStatus).
    /// </summary>
    public async Task<(bool Success, string Message)> TransitionAsync(
        Issue issue, IssueStatus newStatus, string userId, bool isAdmin)
    {
        var oldStatus = issue.Status;

        if (oldStatus == newStatus)
            return (true, "Status unchanged.");

        // Validate transition exists
        if (!TransitionMap.TryGetValue(oldStatus, out var allowedTransitions))
            return (false, $"No transitions defined from {oldStatus}.");

        var transition = allowedTransitions.FirstOrDefault(t => t.To == newStatus);
        if (transition == default)
            return (false, $"Transition from {oldStatus} to {newStatus} is not allowed.");

        // Check admin requirement
        if (transition.RequiresAdmin && !isAdmin)
            return (false, $"Only administrators can transition from {oldStatus} to {newStatus}.");

        // Also check: any roadmap status requires admin
        var roadmapStatuses = new[] { IssueStatus.Planned, IssueStatus.InProgress, IssueStatus.Released };
        if ((roadmapStatuses.Contains(oldStatus) || roadmapStatuses.Contains(newStatus)) && !isAdmin)
            return (false, "Only administrators can manage or change the status of roadmap items.");

        // Execute the transition
        issue.Status = newStatus;

        // Update SLA timestamps
        UpdateSlaTimestamps(issue, newStatus, userId);

        // Log status history
        _context.IssueStatusHistories.Add(new IssueStatusHistory
        {
            IssueId = issue.Id,
            OldStatus = oldStatus,
            NewStatus = newStatus,
            ChangedByUserId = userId,
            ChangedAt = DateTime.UtcNow
        });

        await _context.SaveChangesAsync();

        await _activityService.LogActivityAsync(issue.Id, userId, "StatusChanged",
            $"Changed status from {oldStatus} to {newStatus}", new { oldStatus = oldStatus.ToString(), newStatus = newStatus.ToString() });

        // Send notification to creator
        await NotifyCreatorAsync(issue, oldStatus, newStatus);

        return (true, $"Status changed to {newStatus}.");
    }

    /// <summary>
    /// Close an issue (shortcut that handles roadmap guard + timestamps).
    /// </summary>
    public async Task<(bool Success, string Message)> CloseAsync(Issue issue, string userId, bool isAdmin)
    {
        var roadmapStatuses = new[] { IssueStatus.Planned, IssueStatus.InProgress, IssueStatus.Released };
        if (roadmapStatuses.Contains(issue.Status) && !isAdmin)
            return (false, "Only administrators can close roadmap items.");

        var oldStatus = issue.Status;
        issue.IsClosed = true;
        issue.ClosedAt = DateTime.UtcNow;
        issue.ClosedByUserId = userId;
        issue.Status = IssueStatus.Closed;
        if (issue.ResolvedAt == null) issue.ResolvedAt = DateTime.UtcNow;

        if (oldStatus != IssueStatus.Closed)
        {
            _context.IssueStatusHistories.Add(new IssueStatusHistory
            {
                IssueId = issue.Id,
                OldStatus = oldStatus,
                NewStatus = IssueStatus.Closed,
                ChangedByUserId = userId,
                ChangedAt = DateTime.UtcNow
            });
        }

        await _context.SaveChangesAsync();

        await _activityService.LogActivityAsync(issue.Id, userId, "Closed",
            $"Closed the issue. Status set to Closed from {oldStatus}", new { oldStatus = oldStatus.ToString(), newStatus = "Closed" });

        return (true, "Issue closed.");
    }

    /// <summary>
    /// Reopen a closed issue (shortcut that handles roadmap guard).
    /// </summary>
    public async Task<(bool Success, string Message)> ReopenAsync(Issue issue, string userId, bool isAdmin)
    {
        var roadmapStatuses = new[] { IssueStatus.Planned, IssueStatus.InProgress, IssueStatus.Released };
        if (roadmapStatuses.Contains(issue.Status) && !isAdmin)
            return (false, "Only administrators can reopen roadmap items.");

        var oldStatus = issue.Status;
        issue.IsClosed = false;
        issue.ClosedAt = null;
        issue.ClosedByUserId = null;
        issue.Status = IssueStatus.New;
        issue.ResolvedAt = null;

        if (oldStatus != IssueStatus.New)
        {
            _context.IssueStatusHistories.Add(new IssueStatusHistory
            {
                IssueId = issue.Id,
                OldStatus = oldStatus,
                NewStatus = IssueStatus.New,
                ChangedByUserId = userId,
                ChangedAt = DateTime.UtcNow
            });
        }

        await _context.SaveChangesAsync();

        await _activityService.LogActivityAsync(issue.Id, userId, "Reopened",
            $"Reopened the issue. Status set to New from {oldStatus}", new { oldStatus = oldStatus.ToString(), newStatus = "New" });

        return (true, "Issue reopened.");
    }

    /// <summary>
    /// Check if a transition from current status to target is allowed for the given role.
    /// </summary>
    public bool CanTransition(IssueStatus from, IssueStatus to, bool isAdmin)
    {
        if (!TransitionMap.TryGetValue(from, out var allowedTransitions))
            return false;
        var transition = allowedTransitions.FirstOrDefault(t => t.To == to);
        if (transition == default)
            return false;
        return !transition.RequiresAdmin || isAdmin;
    }

    /// <summary>
    /// Get all valid target statuses from the current status for the given role.
    /// </summary>
    public List<IssueStatus> GetAllowedTransitions(IssueStatus from, bool isAdmin)
    {
        if (!TransitionMap.TryGetValue(from, out var transitions))
            return new List<IssueStatus>();
        return transitions
            .Where(t => !t.RequiresAdmin || isAdmin)
            .Select(t => t.To)
            .ToList();
    }

    // --- Private helpers ---

    private void UpdateSlaTimestamps(Issue issue, IssueStatus newStatus, string userId)
    {
        if (newStatus == IssueStatus.Acknowledged && issue.AcknowledgedAt == null)
            issue.AcknowledgedAt = DateTime.UtcNow;

        if ((newStatus == IssueStatus.Verified || newStatus == IssueStatus.Closed) && issue.ResolvedAt == null)
            issue.ResolvedAt = DateTime.UtcNow;

        if (newStatus == IssueStatus.Closed)
        {
            issue.IsClosed = true;
            issue.ClosedAt = DateTime.UtcNow;
            issue.ClosedByUserId = userId;
        }
        else
        {
            issue.IsClosed = false;
            issue.ClosedAt = null;
            issue.ClosedByUserId = null;
        }
    }

    private async Task NotifyCreatorAsync(Issue issue, IssueStatus oldStatus, IssueStatus newStatus)
    {
        if (issue.CreatorUserId == null) return;
        var creator = await _userManager.FindByIdAsync(issue.CreatorUserId);
        if (creator == null || string.IsNullOrEmpty(creator.Email)) return;

        try
        {
            await _emailService.SendEmailAsync(new MailRequest
            {
                To = creator.Email,
                Subject = $"[Feedback Hub] Issue #{issue.Id} Status Updated",
                Body = $"Hello {creator.Name},\n\nThe status of your issue '{issue.Title}' has been updated from {oldStatus} to {newStatus}.\n\nView details: /app/issues/{issue.Id}"
            });
        }
        catch
        {
            // Email failures should not break the workflow
        }
    }
}
