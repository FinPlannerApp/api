using Application.Contracts;
using Application.DTOs.Issue;
using Application.Services;
using Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace Presentation.Controllers;

public class VoteDto
{
    public int Value { get; set; } // 1 for upvote, -1 for downvote, 0 for unvote
}

public class UpdateIssueDto
{
    public string? Title { get; set; }
    public string? Description { get; set; }
    public string? Type { get; set; }
    public int? CategoryId { get; set; }
    public int? SubcategoryId { get; set; }
    public string? Severity { get; set; }
    public string? Frequency { get; set; }
    public bool? ImpactsMoney { get; set; }
    public decimal? FinancialImpactAmount { get; set; }
    public string? GitHubIssueUrl { get; set; }
    public int? MilestoneId { get; set; }
    public List<int>? LabelIds { get; set; }
    public List<string>? AssigneeUserIds { get; set; }
}

public class UpdateCommentDto
{
    public required string Content { get; set; }
}

public class UpdateStatusDto
{
    public required string Status { get; set; }
}

public class CreateLabelDto
{
    public required string Name { get; set; }
    public string Color { get; set; } = "#6366f1";
    public string? Description { get; set; }
}

public class CreateMilestoneDto
{
    public required string Title { get; set; }
    public string? Description { get; set; }
    public DateTime? DueDate { get; set; }
}

public class ReactionDto
{
    public required string Emoji { get; set; }
}

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class IssuesController : ControllerBase
{
    private readonly IApplicationDbContext _context;
    private readonly IssueRankingService _rankingService;
    private readonly IssueSimilarityService _similarityService;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IEmailService _emailService;
    private readonly GamificationService _gamificationService;

    public IssuesController(IApplicationDbContext context, IssueRankingService rankingService, IssueSimilarityService similarityService, UserManager<ApplicationUser> userManager, IEmailService emailService, GamificationService gamificationService)
    {
        _context = context;
        _rankingService = rankingService;
        _similarityService = similarityService;
        _userManager = userManager;
        _emailService = emailService;
        _gamificationService = gamificationService;
    }

    // Helper: resolve user display name from userId
    private async Task<string> GetDisplayName(string? userId)
    {
        if (string.IsNullOrEmpty(userId)) return "Anonymous";
        var user = await _userManager.FindByIdAsync(userId);
        return user?.Name ?? user?.UserName ?? userId[..Math.Min(8, userId.Length)] + "...";
    }

    private async Task<Dictionary<string, string>> GetDisplayNames(IEnumerable<string?> userIds)
    {
        var distinctIds = userIds.Where(id => !string.IsNullOrEmpty(id)).Distinct().ToList();
        var result = new Dictionary<string, string>();
        foreach (var id in distinctIds)
        {
            result[id!] = await GetDisplayName(id);
        }
        return result;
    }

    // ==================== ISSUES ====================

    [HttpGet]
    [AllowAnonymous]
    public async Task<ActionResult<object>> GetIssues(
        [FromQuery] string? status, 
        [FromQuery] string? type,
        [FromQuery] string? search,
        [FromQuery] int? categoryId,
        [FromQuery] string? severity,
        [FromQuery] string? sort = "pain", 
        [FromQuery] int page = 1, 
        [FromQuery] int pageSize = 10)
    {
        var query = _context.Issues
            .Include(i => i.Category)
            .Include(i => i.Subcategory)
            .Include(i => i.Labels).ThenInclude(la => la.Label)
            .AsNoTracking();

        if (!string.IsNullOrEmpty(status))
            query = query.Where(i => i.Status == status);

        if (!string.IsNullOrEmpty(type) && Enum.TryParse<IssueType>(type, true, out var issueType))
            query = query.Where(i => i.Type == issueType);

        if (!string.IsNullOrEmpty(search))
            query = query.Where(i => EF.Functions.ILike(i.Title, $"%{search}%") || EF.Functions.ILike(i.Description, $"%{search}%"));

        if (categoryId.HasValue)
            query = query.Where(i => i.CategoryId == categoryId.Value);

        if (!string.IsNullOrEmpty(severity))
            query = query.Where(i => i.Severity == severity);

        query = sort switch
        {
            "date" => query.OrderByDescending(i => i.CreatedAt),
            "votes" => query.OrderByDescending(i => i.Votes),
            _ => query.OrderByDescending(i => i.PainScore)
        };

        var totalItems = await query.CountAsync();
        var issues = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

        // Get comment counts
        var issueIds = issues.Select(i => i.Id).ToList();
        var commentCounts = await _context.IssueComments
            .Where(c => issueIds.Contains(c.IssueId))
            .GroupBy(c => c.IssueId)
            .Select(g => new { IssueId = g.Key, Count = g.Count() })
            .ToListAsync();

        // Get display names
        var creatorIds = issues.Select(i => i.CreatorUserId).Distinct().ToList();
        var displayNames = await GetDisplayNames(creatorIds);

        // Get user votes if logged in
        var userId = User.Identity?.IsAuthenticated == true ? User.FindFirstValue(ClaimTypes.NameIdentifier) : null;
        var userVotes = userId != null
            ? await _context.IssueVotes.Where(v => v.UserId == userId && issueIds.Contains(v.IssueId)).ToListAsync()
            : new List<IssueVote>();

        var dtos = issues.Select(i => new IssueDto
        {
            Id = i.Id,
            Title = i.Title,
            Description = i.Description,
            Status = i.Status,
            Type = i.Type.ToString(),
            IsClosed = i.IsClosed,
            PainScore = i.PainScore,
            CategoryName = i.Category?.Name ?? "Uncategorized",
            SubcategoryName = i.Subcategory?.Name ?? "",
            Severity = i.Severity,
            Frequency = i.Frequency,
            ImpactsMoney = i.ImpactsMoney,
            FinancialImpactAmount = i.FinancialImpactAmount,
            CreatedAt = i.CreatedAt,
            UpdatedAt = i.UpdatedAt,
            Votes = i.Votes,
            CommentCount = commentCounts.FirstOrDefault(cc => cc.IssueId == i.Id)?.Count ?? 0,
            CreatorId = i.CreatorUserId,
            CreatorName = i.CreatorUserId != null && displayNames.ContainsKey(i.CreatorUserId) ? displayNames[i.CreatorUserId] : "Anonymous",
            UserVote = userVotes.FirstOrDefault(v => v.IssueId == i.Id)?.Value ?? 0,
            Labels = i.Labels.Select(la => new IssueLabelDto { Id = la.LabelId, Name = la.Label?.Name ?? "", Color = la.Label?.Color ?? "#6366f1" }).ToList(),
            GitHubIssueUrl = i.GitHubIssueUrl
        }).ToList();

        return Ok(new { data = dtos, totalItems, page, pageSize });
    }

    [HttpGet("{id}")]
    [AllowAnonymous]
    public async Task<ActionResult> GetIssueDetail(int id)
    {
        var issue = await _context.Issues
            .Include(i => i.Category)
            .Include(i => i.Subcategory)
            .Include(i => i.Labels).ThenInclude(la => la.Label)
            .Include(i => i.Assignees)
            .Include(i => i.Milestone)
            .Include(i => i.StatusHistory)
            .AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == id);

        if (issue == null) return NotFound();

        var userId = User.Identity?.IsAuthenticated == true ? User.FindFirstValue(ClaimTypes.NameIdentifier) : null;
        var userVote = userId != null ? await _context.IssueVotes.FirstOrDefaultAsync(v => v.IssueId == id && v.UserId == userId) : null;
        var commentCount = await _context.IssueComments.CountAsync(c => c.IssueId == id);
        var creatorName = await GetDisplayName(issue.CreatorUserId);
        var closedByName = issue.IsClosed ? await GetDisplayName(issue.ClosedByUserId) : null;

        // Resolve assignee names
        var assigneeIds = issue.Assignees.Select(a => a.UserId).ToList();
        var assigneeNames = await GetDisplayNames(assigneeIds);
        var assignees = issue.Assignees.Select(a => new { userId = a.UserId, displayName = assigneeNames.GetValueOrDefault(a.UserId, "Unknown") }).ToList();

        var labels = issue.Labels.Select(la => new { id = la.LabelId, name = la.Label?.Name ?? "", color = la.Label?.Color ?? "#6366f1" }).ToList();

        var statusHistory = new List<object>();
        if (issue.StatusHistory != null)
        {
            var historyList = issue.StatusHistory.OrderBy(h => h.ChangedAt).ToList();
            var changerIds = historyList.Select(h => h.ChangedByUserId).Where(uid => uid != null).Distinct().ToList();
            var changerNames = await GetDisplayNames(changerIds);

            foreach (var h in historyList)
            {
                statusHistory.Add(new
                {
                    id = h.Id,
                    oldStatus = h.OldStatus,
                    newStatus = h.NewStatus,
                    changedByUserId = h.ChangedByUserId,
                    changedByName = h.ChangedByUserId != null && changerNames.ContainsKey(h.ChangedByUserId)
                        ? changerNames[h.ChangedByUserId]
                        : "System",
                    changedAt = h.ChangedAt
                });
            }
        }

        return Ok(new
        {
            id = issue.Id,
            title = issue.Title,
            description = issue.Description,
            status = issue.Status,
            type = issue.Type.ToString(),
            isClosed = issue.IsClosed,
            closedAt = issue.ClosedAt,
            closedByName,
            painScore = issue.PainScore,
            categoryName = issue.Category?.Name ?? "Uncategorized",
            subcategoryName = issue.Subcategory?.Name ?? "",
            severity = issue.Severity,
            frequency = issue.Frequency,
            impactsMoney = issue.ImpactsMoney,
            financialImpactAmount = issue.FinancialImpactAmount,
            createdAt = issue.CreatedAt,
            updatedAt = issue.UpdatedAt,
            votes = issue.Votes,
            creatorId = issue.CreatorUserId,
            creatorName,
            userVote = userVote?.Value ?? 0,
            commentCount,
            labels,
            assignees,
            milestoneId = issue.MilestoneId,
            milestoneTitle = issue.Milestone?.Title,
            gitHubIssueUrl = issue.GitHubIssueUrl,
            statusHistory = statusHistory
        });
    }

    [HttpPost("check-similar")]
    public async Task<ActionResult<List<IssueDto>>> CheckSimilar([FromBody] CreateIssueDto input)
    {
        var similar = await _similarityService.FindSimilarIssuesAsync(input.Title, input.Description);
        return Ok(similar.Select(i => new IssueDto { 
            Id = i.Id, 
            Title = i.Title, 
            Description = i.Description,
            Status = i.Status,
            PainScore = i.PainScore,
            CategoryName = i.Category?.Name ?? "Similar Match", 
            CreatedAt = i.CreatedAt
        }).ToList());
    }

    [HttpPost]
    [Authorize]
    public async Task<ActionResult<int>> Create([FromBody] CreateIssueDto input)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var issueType = Enum.TryParse<IssueType>(input.Type, true, out var parsed) ? parsed : IssueType.Bug;
        
        var issue = new Domain.Entities.Issue
        {
            Title = input.Title,
            Description = input.Description,
            Priority = input.Priority,
            Type = issueType,
            CategoryId = input.CategoryId,
            SubcategoryId = input.SubcategoryId,
            Severity = input.Severity,
            ImpactsMoney = input.ImpactsMoney,
            FinancialImpactAmount = input.FinancialImpactAmount,
            Frequency = input.Frequency,
            Status = "New",
            CreatorUserId = userId,
            GitHubIssueUrl = input.GitHubIssueUrl
        };
        
        issue.PainScore = _rankingService.CalculatePainScore(issue);

        _context.Issues.Add(issue);
        await _context.SaveChangesAsync();

        // Create initial status history log
        var initialHistory = new IssueStatusHistory
        {
            IssueId = issue.Id,
            OldStatus = "None",
            NewStatus = "New",
            ChangedByUserId = userId ?? "System",
            ChangedAt = DateTime.UtcNow
        };
        _context.IssueStatusHistories.Add(initialHistory);
        await _context.SaveChangesAsync();

        // Trigger karma recalculation so the user appears on the leaderboard
        if (userId != null)
        {
            await _gamificationService.RecalculateKarma(userId);
        }

        return Ok(issue.Id);
    }

    [HttpPut("{id}")]
    [Authorize]
    public async Task<ActionResult> UpdateIssue(int id, [FromBody] UpdateIssueDto input)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var issue = await _context.Issues.FindAsync(id);
        if (issue == null) return NotFound();

        var isAdmin = User.IsInRole("Admin");

        // 1. Only creator or Admin can edit
        if (issue.CreatorUserId != userId && !isAdmin)
            return Ok(new { success = false, message = "Only the issue creator or an administrator can edit this issue." });

        // 2. If the issue is already in a roadmap status, only Admins can edit it
        var isRoadmapStatus = issue.Status == "Planned" || issue.Status == "InProgress" || issue.Status == "Released";
        if (isRoadmapStatus && !isAdmin)
            return Ok(new { success = false, message = "Only administrators can edit active roadmap items." });

        if (input.Title != null) issue.Title = input.Title;
        if (input.Description != null) issue.Description = input.Description;
        if (input.Type != null && Enum.TryParse<IssueType>(input.Type, true, out var t)) issue.Type = t;
        if (input.CategoryId.HasValue) issue.CategoryId = input.CategoryId;
        if (input.SubcategoryId.HasValue) issue.SubcategoryId = input.SubcategoryId;
        if (input.Severity != null) issue.Severity = input.Severity;
        if (input.Frequency != null) issue.Frequency = input.Frequency;
        if (input.ImpactsMoney.HasValue) issue.ImpactsMoney = input.ImpactsMoney.Value;
        if (input.FinancialImpactAmount.HasValue) issue.FinancialImpactAmount = input.FinancialImpactAmount;

        issue.PainScore = _rankingService.CalculatePainScore(issue);
        await _context.SaveChangesAsync();

        return Ok(new { success = true, message = "Issue updated successfully." });
    }

    [HttpPut("{id}/status")]
    [Authorize]
    public async Task<ActionResult> UpdateStatus(int id, [FromBody] UpdateStatusDto input)
    {
        var issue = await _context.Issues.FindAsync(id);
        if (issue == null) return NotFound();

        var validStatuses = new[] { "New", "Acknowledged", "Triaged", "Planned", "InProgress", "Released", "Verified", "Closed" };
        if (!validStatuses.Contains(input.Status))
            return BadRequest(new { message = "Invalid status value." });

        var isAdmin = User.IsInRole("Admin");

        // 3. Restrict transitions into, within, or out of roadmap statuses to Admins
        var currentStatusIsRoadmap = issue.Status == "Planned" || issue.Status == "InProgress" || issue.Status == "Released";
        var newStatusIsRoadmap = input.Status == "Planned" || input.Status == "InProgress" || input.Status == "Released";

        if ((currentStatusIsRoadmap || newStatusIsRoadmap) && !isAdmin)
        {
            return Ok(new { success = false, message = "Only administrators can manage or change the status of roadmap items." });
        }

        if (input.Status == "Acknowledged" && issue.AcknowledgedAt == null)
        {
            issue.AcknowledgedAt = DateTime.UtcNow;
        }
        
        if ((input.Status == "Verified" || input.Status == "Closed") && issue.ResolvedAt == null)
        {
            issue.ResolvedAt = DateTime.UtcNow;
        }
        
        if (input.Status == "Closed")
        {
            issue.IsClosed = true;
            issue.ClosedAt = DateTime.UtcNow;
            issue.ClosedByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        }
        else
        {
            issue.IsClosed = false;
            issue.ClosedAt = null;
            issue.ClosedByUserId = null;
        }

        var oldStatus = issue.Status;
        issue.Status = input.Status;

        if (oldStatus != input.Status)
        {
            _context.IssueStatusHistories.Add(new IssueStatusHistory
            {
                IssueId = issue.Id,
                OldStatus = oldStatus,
                NewStatus = input.Status,
                ChangedByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "System",
                ChangedAt = DateTime.UtcNow
            });
        }

        await _context.SaveChangesAsync();

        if (issue.CreatorUserId != null && oldStatus != input.Status)
        {
            var creator = await _userManager.FindByIdAsync(issue.CreatorUserId);
            if (creator != null && !string.IsNullOrEmpty(creator.Email))
            {
                await _emailService.SendEmailAsync(new Application.DTOs.Auth.MailRequest
                {
                    To = creator.Email,
                    Subject = $"[Feedback Hub] Issue #{issue.Id} Status Updated",
                    Body = $"Hello {creator.Name},\n\nThe status of your issue '{issue.Title}' has been updated to {input.Status}.\n\nView details: /app/issues/{issue.Id}"
                });
            }
        }

        return Ok(new { success = true, message = "Status updated.", status = issue.Status });
    }

    // ==================== VOTING ====================

    [HttpPost("{id}/vote")]
    [Authorize]
    public async Task<ActionResult> ToggleVote(int id, [FromBody] VoteDto input)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId == null) return Unauthorized();

        var issue = await _context.Issues.FindAsync(id);
        if (issue == null) return NotFound();

        if (issue.CreatorUserId == userId)
        {
            return Ok(new { success = false, message = "You cannot vote on your own issue.", votes = issue.Votes, painScore = issue.PainScore, userVote = 0 });
        }

        var existingVote = await _context.IssueVotes.FirstOrDefaultAsync(v => v.IssueId == id && v.UserId == userId);
        string message;
        int newUserVote;

        if (existingVote != null)
        {
            if (input.Value == 0) // Explicit unvote
            {
                _context.IssueVotes.Remove(existingVote);
                issue.Votes -= existingVote.Value;
                issue.PainScore -= existingVote.Value * 10;
                message = "Vote removed.";
                newUserVote = 0;
            }
            else if (existingVote.Value == input.Value) // Same vote again = already voted, do nothing
            {
                message = "You already voted this way.";
                newUserVote = existingVote.Value;
                return Ok(new { success = true, message, votes = issue.Votes, painScore = issue.PainScore, userVote = newUserVote });
            }
            else // Switching vote direction
            {
                issue.Votes -= existingVote.Value;
                issue.PainScore -= existingVote.Value * 10;
                
                existingVote.Value = input.Value;
                
                issue.Votes += input.Value;
                issue.PainScore += input.Value * 10;
                message = "Vote updated.";
                newUserVote = input.Value;
            }
        }
        else
        {
            if (input.Value != 0)
            {
                var vote = new IssueVote { IssueId = id, UserId = userId, Value = input.Value };
                _context.IssueVotes.Add(vote);
                issue.Votes += input.Value;
                issue.PainScore += input.Value * 10;
                message = "Vote recorded.";
                newUserVote = input.Value;
            }
            else
            {
                message = "No vote to remove.";
                newUserVote = 0;
            }
        }

        await _context.SaveChangesAsync();

        // Recalculate karma for the issue creator
        if (issue.CreatorUserId != null)
        {
            await _gamificationService.RecalculateKarma(issue.CreatorUserId);
        }

        return Ok(new { success = true, message, votes = issue.Votes, painScore = issue.PainScore, userVote = newUserVote });
    }

    [HttpGet("{id}/voters")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult> GetIssueVoters(int id)
    {
        var issue = await _context.Issues.FindAsync(id);
        if (issue == null) return NotFound();

        var votes = await _context.IssueVotes
            .Where(v => v.IssueId == id)
            .ToListAsync();

        var userIds = votes.Select(v => v.UserId).Distinct().ToList();
        var displayNames = await GetDisplayNames(userIds);

        var result = votes.Select(v => new
        {
            UserId = v.UserId,
            DisplayName = displayNames.ContainsKey(v.UserId) ? displayNames[v.UserId] : "Unknown",
            Value = v.Value
        }).ToList();

        return Ok(result);
    }

    // ==================== COMMENTS ====================

    [HttpGet("{id}/comments")]
    [AllowAnonymous]
    public async Task<ActionResult> GetComments(int id)
    {
        var comments = await _context.IssueComments
            .Where(c => c.IssueId == id)
            .OrderBy(c => c.CreatedAt)
            .AsNoTracking()
            .ToListAsync();

        var userId = User.Identity?.IsAuthenticated == true ? User.FindFirstValue(ClaimTypes.NameIdentifier) : null;
        var commentIds = comments.Select(c => c.Id).ToList();
        var userCommentVotes = userId != null
            ? await _context.CommentVotes.Where(v => v.UserId == userId && commentIds.Contains(v.CommentId)).ToListAsync()
            : new List<CommentVote>();

        // Resolve display names
        var creatorIds = comments.Select(c => c.CreatorUserId).Distinct();
        var displayNames = await GetDisplayNames(creatorIds);

        var result = comments.Select(c => new
        {
            id = c.Id,
            content = c.Content,
            creatorUserId = c.CreatorUserId,
            creatorName = c.CreatorUserId != null && displayNames.ContainsKey(c.CreatorUserId) ? displayNames[c.CreatorUserId] : "Anonymous",
            createdAt = c.CreatedAt,
            updatedAt = c.UpdatedAt,
            parentCommentId = c.ParentCommentId,
            score = c.Score,
            userVote = userCommentVotes.FirstOrDefault(v => v.CommentId == c.Id)?.Value ?? 0,
            isEdited = c.UpdatedAt > c.CreatedAt.AddSeconds(5) // Show "edited" if updated > 5s after creation
        });

        return Ok(result);
    }

    [HttpPost("{id}/comments")]
    [Authorize]
    public async Task<ActionResult> AddComment(int id, [FromBody] CreateCommentDto input)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var comment = new IssueComment
        {
            IssueId = id,
            Content = input.Content,
            StructuredMetadata = input.StructuredMetadata,
            IsHelpful = false,
            CreatorUserId = userId,
            ParentCommentId = input.ParentCommentId
        };
        
        _context.IssueComments.Add(comment);
        await _context.SaveChangesAsync();

        // Notify issue creator about new comment
        var issue = await _context.Issues.FindAsync(id);
        if (issue?.CreatorUserId != null && issue.CreatorUserId != userId)
        {
            var issueCreator = await _userManager.FindByIdAsync(issue.CreatorUserId);
            if (issueCreator != null && !string.IsNullOrEmpty(issueCreator.Email))
            {
                var commenterName = await GetDisplayName(userId);
                await _emailService.SendEmailAsync(new Application.DTOs.Auth.MailRequest
                {
                    To = issueCreator.Email,
                    Subject = $"[Feedback Hub] New comment on your issue #{issue.Id}",
                    Body = $"Hello {issueCreator.Name},\n\n{commenterName} commented on your issue '{issue.Title}'.\n\nView details: /app/issues/{issue.Id}"
                });
            }
        }

        var creatorName = await GetDisplayName(userId);
        return Ok(new { id = comment.Id, content = comment.Content, creatorUserId = userId, creatorName, createdAt = comment.CreatedAt, parentCommentId = comment.ParentCommentId, score = 0, userVote = 0 });
    }

    [HttpPut("comments/{commentId}")]
    [Authorize]
    public async Task<ActionResult> EditComment(int commentId, [FromBody] UpdateCommentDto input)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var comment = await _context.IssueComments.FindAsync(commentId);
        if (comment == null) return NotFound();
        if (comment.CreatorUserId != userId)
            return Ok(new { success = false, message = "Only the comment author can edit this comment." });

        comment.Content = input.Content;
        await _context.SaveChangesAsync();
        return Ok(new { success = true, message = "Comment updated.", content = comment.Content, updatedAt = comment.UpdatedAt });
    }

    [HttpDelete("comments/{commentId}")]
    [Authorize]
    public async Task<ActionResult> DeleteComment(int commentId)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var comment = await _context.IssueComments.FindAsync(commentId);
        if (comment == null) return NotFound();
        if (comment.CreatorUserId != userId)
            return Ok(new { success = false, message = "Only the comment author can delete this comment." });

        // Check if comment has replies — soft delete by replacing content
        var hasReplies = await _context.IssueComments.AnyAsync(c => c.ParentCommentId == commentId);
        if (hasReplies)
        {
            comment.Content = "[This comment has been deleted]";
            comment.CreatorUserId = null;
        }
        else
        {
            // Also delete any votes for this comment
            var votes = await _context.CommentVotes.Where(v => v.CommentId == commentId).ToListAsync();
            _context.CommentVotes.RemoveRange(votes);
            _context.IssueComments.Remove(comment);
        }

        await _context.SaveChangesAsync();
        return Ok(new { success = true, message = "Comment deleted." });
    }

    [HttpPost("comments/{commentId}/vote")]
    [Authorize]
    public async Task<ActionResult> VoteComment(int commentId, [FromBody] VoteDto input)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId == null) return Unauthorized();

        var comment = await _context.IssueComments.FindAsync(commentId);
        if (comment == null) return NotFound();

        if (comment.CreatorUserId == userId)
            return Ok(new { success = false, message = "Cannot vote on your own comment.", score = comment.Score, userVote = 0 });

        var existing = await _context.CommentVotes.FirstOrDefaultAsync(v => v.CommentId == commentId && v.UserId == userId);
        int newUserVote;

        if (existing != null)
        {
            if (input.Value == 0)
            {
                _context.CommentVotes.Remove(existing);
                comment.Score -= existing.Value;
                newUserVote = 0;
            }
            else if (existing.Value == input.Value) // Same vote = no change
            {
                return Ok(new { success = true, message = "Already voted this way.", score = comment.Score, userVote = existing.Value });
            }
            else
            {
                comment.Score -= existing.Value;
                existing.Value = input.Value;
                comment.Score += input.Value;
                newUserVote = input.Value;
            }
        }
        else if (input.Value != 0)
        {
            _context.CommentVotes.Add(new CommentVote { CommentId = commentId, UserId = userId, Value = input.Value });
            comment.Score += input.Value;
            newUserVote = input.Value;
        }
        else
        {
            newUserVote = 0;
        }

        await _context.SaveChangesAsync();
        return Ok(new { success = true, message = "Vote updated.", score = comment.Score, userVote = newUserVote });
    }

    // ==================== TAXONOMY ====================

    [HttpGet("taxonomy")]
    [AllowAnonymous]
    public async Task<ActionResult<List<IssueTaxonomy>>> GetTaxonomies()
    {
        var list = await _context.IssueTaxonomies
            .Include(t => t.Children)
            .Where(t => t.ParentId == null) // Root categories
            .ToListAsync();
        return Ok(list);
    }

    [HttpPost("taxonomy")]
    public async Task<ActionResult<IssueTaxonomy>> CreateTaxonomy([FromBody] IssueTaxonomy input)
    {
        var taxonomy = new IssueTaxonomy
        {
            Name = input.Name,
            Type = input.ParentId == null ? "Category" : "Subcategory",
            ParentId = input.ParentId
        };
        _context.IssueTaxonomies.Add(taxonomy);
        await _context.SaveChangesAsync();
        return Ok(taxonomy);
    }

    [HttpPost("seed")]
    public async Task<ActionResult> SeedTaxonomy([FromServices] TaxonomySeederService seeder)
    {
        await seeder.SeedAsync();
        return Ok("Taxonomy seeded.");
    }

    // ==================== CLOSE / REOPEN ====================

    [HttpPost("{id}/close")]
    [Authorize]
    public async Task<ActionResult> CloseIssue(int id)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var issue = await _context.Issues.FindAsync(id);
        if (issue == null) return NotFound();

        var isAdmin = User.IsInRole("Admin");
        var isRoadmapStatus = issue.Status == "Planned" || issue.Status == "InProgress" || issue.Status == "Released";
        if (isRoadmapStatus && !isAdmin)
        {
            return Ok(new { success = false, message = "Only administrators can close roadmap items." });
        }

        var oldStatus = issue.Status;
        issue.IsClosed = true;
        issue.ClosedAt = DateTime.UtcNow;
        issue.ClosedByUserId = userId;
        issue.Status = "Closed";
        if (issue.ResolvedAt == null) issue.ResolvedAt = DateTime.UtcNow;

        if (oldStatus != "Closed")
        {
            _context.IssueStatusHistories.Add(new IssueStatusHistory
            {
                IssueId = issue.Id,
                OldStatus = oldStatus,
                NewStatus = "Closed",
                ChangedByUserId = userId ?? "System",
                ChangedAt = DateTime.UtcNow
            });
        }

        await _context.SaveChangesAsync();
        var closedByName = await GetDisplayName(userId);
        return Ok(new { success = true, message = "Issue closed.", isClosed = true, closedAt = issue.ClosedAt, closedByName });
    }

    [HttpPost("{id}/reopen")]
    [Authorize]
    public async Task<ActionResult> ReopenIssue(int id)
    {
        var issue = await _context.Issues.FindAsync(id);
        if (issue == null) return NotFound();

        var isAdmin = User.IsInRole("Admin");
        var isRoadmapStatus = issue.Status == "Planned" || issue.Status == "InProgress" || issue.Status == "Released";
        if (isRoadmapStatus && !isAdmin)
        {
            return Ok(new { success = false, message = "Only administrators can reopen roadmap items." });
        }

        var oldStatus = issue.Status;
        issue.IsClosed = false;
        issue.ClosedAt = null;
        issue.ClosedByUserId = null;
        issue.Status = "New";
        issue.ResolvedAt = null; // Reset SLA if reopened

        if (oldStatus != "New")
        {
            _context.IssueStatusHistories.Add(new IssueStatusHistory
            {
                IssueId = issue.Id,
                OldStatus = oldStatus,
                NewStatus = "New",
                ChangedByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "System",
                ChangedAt = DateTime.UtcNow
            });
        }

        await _context.SaveChangesAsync();
        return Ok(new { success = true, message = "Issue reopened.", isClosed = false });
    }

    // ==================== LABELS ====================

    [HttpGet("labels")]
    [AllowAnonymous]
    public async Task<ActionResult> GetLabels()
    {
        var labels = await _context.IssueLabels.OrderBy(l => l.Name).ToListAsync();
        return Ok(labels.Select(l => new IssueLabelDto { Id = l.Id, Name = l.Name, Color = l.Color, Description = l.Description }));
    }

    [HttpPost("labels")]
    [Authorize]
    public async Task<ActionResult> CreateLabel([FromBody] CreateLabelDto input)
    {
        var label = new IssueLabel { Name = input.Name, Color = input.Color, Description = input.Description };
        _context.IssueLabels.Add(label);
        await _context.SaveChangesAsync();
        return Ok(new IssueLabelDto { Id = label.Id, Name = label.Name, Color = label.Color, Description = label.Description });
    }

    [HttpPost("{id}/labels/{labelId}")]
    [Authorize]
    public async Task<ActionResult> AddLabel(int id, int labelId)
    {
        var exists = await _context.IssueLabelAssignments.AnyAsync(la => la.IssueId == id && la.LabelId == labelId);
        if (exists) return Ok(new { success = true, message = "Label already assigned." });
        _context.IssueLabelAssignments.Add(new IssueLabelAssignment { IssueId = id, LabelId = labelId });
        await _context.SaveChangesAsync();
        return Ok(new { success = true, message = "Label added." });
    }

    [HttpDelete("{id}/labels/{labelId}")]
    [Authorize]
    public async Task<ActionResult> RemoveLabel(int id, int labelId)
    {
        var assignment = await _context.IssueLabelAssignments.FirstOrDefaultAsync(la => la.IssueId == id && la.LabelId == labelId);
        if (assignment == null) return NotFound();
        _context.IssueLabelAssignments.Remove(assignment);
        await _context.SaveChangesAsync();
        return Ok(new { success = true, message = "Label removed." });
    }

    // ==================== ASSIGNEES ====================

    [HttpPost("{id}/assignees")]
    [Authorize]
    public async Task<ActionResult> AddAssignee(int id, [FromBody] AssigneeDto input)
    {
        var exists = await _context.IssueAssignees.AnyAsync(a => a.IssueId == id && a.UserId == input.UserId);
        if (exists) return Ok(new { success = true, message = "Already assigned." });
        _context.IssueAssignees.Add(new IssueAssignee { IssueId = id, UserId = input.UserId });
        await _context.SaveChangesAsync();
        var name = await GetDisplayName(input.UserId);
        return Ok(new { success = true, message = "Assignee added.", userId = input.UserId, displayName = name });
    }

    [HttpDelete("{id}/assignees/{assigneeUserId}")]
    [Authorize]
    public async Task<ActionResult> RemoveAssignee(int id, string assigneeUserId)
    {
        var a = await _context.IssueAssignees.FirstOrDefaultAsync(a => a.IssueId == id && a.UserId == assigneeUserId);
        if (a == null) return NotFound();
        _context.IssueAssignees.Remove(a);
        await _context.SaveChangesAsync();
        return Ok(new { success = true, message = "Assignee removed." });
    }

    // ==================== MILESTONES ====================

    [HttpGet("milestones")]
    [AllowAnonymous]
    public async Task<ActionResult> GetMilestones()
    {
        var milestones = await _context.IssueMilestones.Include(m => m.Issues).OrderByDescending(m => m.CreatedAt).ToListAsync();
        return Ok(milestones.Select(m => new MilestoneDto
        {
            Id = m.Id, Title = m.Title, Description = m.Description, DueDate = m.DueDate, IsClosed = m.IsClosed,
            OpenCount = m.Issues.Count(i => !i.IsClosed), ClosedCount = m.Issues.Count(i => i.IsClosed),
            Progress = m.Issues.Count > 0 ? Math.Round(m.Issues.Count(i => i.IsClosed) * 100.0 / m.Issues.Count, 1) : 0
        }));
    }

    [HttpPost("milestones")]
    [Authorize]
    public async Task<ActionResult> CreateMilestone([FromBody] CreateMilestoneDto input)
    {
        var milestone = new IssueMilestone { Title = input.Title, Description = input.Description, DueDate = input.DueDate };
        _context.IssueMilestones.Add(milestone);
        await _context.SaveChangesAsync();
        return Ok(new MilestoneDto { Id = milestone.Id, Title = milestone.Title, Description = milestone.Description, DueDate = milestone.DueDate });
    }

    // ==================== REACTIONS ====================

    [HttpPost("comments/{commentId}/reactions")]
    [Authorize]
    public async Task<ActionResult> ToggleReaction(int commentId, [FromBody] ReactionDto input)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId == null) return Unauthorized();
        var validEmojis = new[] { "👍", "👎", "❤️", "🎉", "😄", "😕", "👀", "🚀" };
        if (!validEmojis.Contains(input.Emoji)) return BadRequest("Invalid emoji.");

        var existing = await _context.CommentReactions.FirstOrDefaultAsync(r => r.CommentId == commentId && r.UserId == userId && r.Emoji == input.Emoji);
        if (existing != null)
        {
            _context.CommentReactions.Remove(existing);
            await _context.SaveChangesAsync();
            return Ok(new { success = true, action = "removed" });
        }
        _context.CommentReactions.Add(new CommentReaction { CommentId = commentId, UserId = userId, Emoji = input.Emoji });
        await _context.SaveChangesAsync();
        return Ok(new { success = true, action = "added" });
    }

    [HttpGet("comments/{commentId}/reactions")]
    [AllowAnonymous]
    public async Task<ActionResult> GetReactions(int commentId)
    {
        var reactions = await _context.CommentReactions.Where(r => r.CommentId == commentId).ToListAsync();
        var userId = User.Identity?.IsAuthenticated == true ? User.FindFirstValue(ClaimTypes.NameIdentifier) : null;
        var grouped = reactions.GroupBy(r => r.Emoji).Select(g => new { emoji = g.Key, count = g.Count(), reacted = userId != null && g.Any(r => r.UserId == userId) });
        return Ok(grouped);
    }

    // ==================== ATTACHMENTS ====================
    [HttpPost("{id}/attachments")]
    [Authorize]
    public async Task<ActionResult> UploadAttachment(int id, IFormFile file)
    {
        var issue = await _context.Issues.FindAsync(id);
        if (issue == null) return NotFound();
        
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId == null) return Unauthorized();

        var uploadsPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "issues", id.ToString());
        Directory.CreateDirectory(uploadsPath);

        var safeFileName = Path.GetFileName(file.FileName);
        var filePath = Path.Combine(uploadsPath, safeFileName);
        using (var stream = new FileStream(filePath, FileMode.Create))
        {
            await file.CopyToAsync(stream);
        }

        var attachment = new IssueAttachment
        {
            IssueId = id,
            FileName = safeFileName,
            FilePath = $"/uploads/issues/{id}/{safeFileName}",
            ContentType = file.ContentType,
            FileSizeBytes = file.Length,
            UploadedByUserId = userId
        };

        _context.IssueAttachments.Add(attachment);
        await _context.SaveChangesAsync();
        
        return Ok(attachment);
    }

    [HttpGet("{id}/attachments")]
    [AllowAnonymous]
    public async Task<ActionResult> GetAttachments(int id)
    {
        var attachments = await _context.IssueAttachments.Where(a => a.IssueId == id).ToListAsync();
        return Ok(attachments);
    }

    // ==================== ANALYTICS ====================
    [HttpGet("analytics")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult> GetAnalytics()
    {
        var totalIssues = await _context.Issues.CountAsync();
        var openIssues = await _context.Issues.CountAsync(i => !i.IsClosed);
        var closedIssues = await _context.Issues.CountAsync(i => i.IsClosed);
        
        var issuesByStatus = await _context.Issues.GroupBy(i => i.Status).Select(g => new { Status = g.Key, Count = g.Count() }).ToListAsync();
        var issuesByType = await _context.Issues.GroupBy(i => i.Type).Select(g => new { Type = g.Key.ToString(), Count = g.Count() }).ToListAsync();
        
        var ackIssues = await _context.Issues.Where(i => i.AcknowledgedAt != null).ToListAsync();
        var avgAckTime = ackIssues.Any() ? ackIssues.Average(i => (i.AcknowledgedAt.Value - i.CreatedAt).TotalHours) : 0;
        
        var resolvedIssues = await _context.Issues.Where(i => i.ResolvedAt != null).ToListAsync();
        var avgResolveTime = resolvedIssues.Any() ? resolvedIssues.Average(i => (i.ResolvedAt.Value - i.CreatedAt).TotalHours) : 0;

        return Ok(new
        {
            TotalIssues = totalIssues,
            OpenIssues = openIssues,
            ClosedIssues = closedIssues,
            IssuesByStatus = issuesByStatus,
            IssuesByType = issuesByType,
            AvgAckTimeHours = avgAckTime,
            AvgResolveTimeHours = avgResolveTime
        });
    }

    // ==================== GAMIFICATION ====================
    [HttpGet("leaderboard")]
    [AllowAnonymous]
    public async Task<ActionResult> GetLeaderboard()
    {
        var userIds = await _userManager.Users.Select(u => u.Id).ToListAsync();
        var existingProfileUserIds = await _context.UserGamificationProfiles
            .Select(p => p.UserId)
            .ToListAsync();
        var existingProfileSet = new HashSet<string>(existingProfileUserIds);

        var missingUserIds = userIds.Where(uid => !existingProfileSet.Contains(uid)).ToList();
        foreach (var missingUserId in missingUserIds)
        {
            await _gamificationService.RecalculateKarma(missingUserId);
        }

        var profiles = await _context.UserGamificationProfiles
            .OrderByDescending(p => p.KarmaScore)
            .Take(10)
            .ToListAsync();

        var profileUserIds = profiles.Select(p => p.UserId).ToList();
        var profileUsers = await _userManager.Users
            .Where(u => profileUserIds.Contains(u.Id))
            .ToListAsync();
        var userMap = profileUsers.ToDictionary(u => u.Id);

        var allUserBadges = await _context.UserBadges
            .Where(ub => profileUserIds.Contains(ub.UserId))
            .Include(ub => ub.Badge)
            .ToListAsync();
        var badgesMap = allUserBadges.GroupBy(ub => ub.UserId).ToDictionary(g => g.Key, g => g.ToList());

        var result = new List<object>();
        foreach (var p in profiles)
        {
            var user = userMap.GetValueOrDefault(p.UserId);
            var displayName = user?.Name ?? user?.UserName ?? p.UserId[..Math.Min(8, p.UserId.Length)] + "...";
            var badges = badgesMap.GetValueOrDefault(p.UserId) ?? new List<UserBadge>();

            result.Add(new
            {
                UserId = p.UserId,
                DisplayName = displayName,
                KarmaScore = p.KarmaScore,
                Tag = p.ContributorTag,
                Badges = badges.Select(b => new { b.Badge?.Name, b.Badge?.IconUrl, b.Badge?.Color, b.AwardedAt })
            });
        }
        return Ok(result);
    }

    [HttpGet("gamification/badges")]
    [AllowAnonymous]
    public async Task<ActionResult> GetAllBadges()
    {
        var badges = await _context.Badges.ToListAsync();
        return Ok(badges);
    }

    [HttpGet("gamification/profile")]
    [Authorize]
    public async Task<ActionResult> GetMyGamificationProfile()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId == null) return Unauthorized();

        var profile = await _context.UserGamificationProfiles.FindAsync(userId);
        var badges = await _context.UserBadges.Where(ub => ub.UserId == userId).Include(ub => ub.Badge).ToListAsync();

        return Ok(new
        {
            KarmaScore = profile?.KarmaScore ?? 0,
            Tag = profile?.ContributorTag,
            Badges = badges.Select(b => new { b.Badge?.Name, b.Badge?.Description, b.Badge?.IconUrl, b.Badge?.Color, b.AwardedAt })
        });
    }
}
