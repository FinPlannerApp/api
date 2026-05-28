using Application.Contracts;
using Application.DTOs.Auth;
using Application.DTOs.Issue;
using Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Application.Services;

/// <summary>
/// Handles all comment operations: CRUD, voting, and email notifications.
/// </summary>
public class CommentService
{
    private readonly IApplicationDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IEmailService _emailService;
    private readonly GamificationService _gamificationService;
    private readonly IssueActivityService _activityService;

    public CommentService(
        IApplicationDbContext context,
        UserManager<ApplicationUser> userManager,
        IEmailService emailService,
        GamificationService gamificationService,
        IssueActivityService activityService)
    {
        _context = context;
        _userManager = userManager;
        _emailService = emailService;
        _gamificationService = gamificationService;
        _activityService = activityService;
    }

    /// <summary>
    /// Get all comments for an issue with user vote state.
    /// </summary>
    public async Task<List<object>> GetCommentsAsync(int issueId, string? currentUserId)
    {
        var comments = await _context.IssueComments
            .Where(c => c.IssueId == issueId)
            .OrderBy(c => c.CreatedAt)
            .AsNoTracking()
            .ToListAsync();

        var commentIds = comments.Select(c => c.Id).ToList();
        var userCommentVotes = currentUserId != null
            ? await _context.CommentVotes.Where(v => v.UserId == currentUserId && commentIds.Contains(v.CommentId)).ToListAsync()
            : new List<CommentVote>();

        // Resolve display names
        var creatorIds = comments.Select(c => c.CreatorUserId).Distinct();
        var displayNames = await GetDisplayNames(creatorIds);

        return comments.Select(c => (object)new
        {
            id = c.Id,
            content = c.Content,
            creatorUserId = c.CreatorUserId,
            creatorName = c.CreatorUserId != null && displayNames.ContainsKey(c.CreatorUserId) ? displayNames[c.CreatorUserId] : "Anonymous",
            createdAt = c.CreatedAt,
            updatedAt = c.UpdatedAt,
            parentCommentId = c.ParentCommentId,
            score = c.Score,
            type = c.Type.ToString(),
            userVote = userCommentVotes.FirstOrDefault(v => v.CommentId == c.Id)?.Value ?? 0,
            isEdited = c.UpdatedAt > c.CreatedAt.AddSeconds(5),
            isHelpful = c.IsHelpful,
            isRootCause = c.IsRootCause,
            isReproConfirmed = c.IsReproConfirmed
        }).ToList();
    }

    /// <summary>
    /// Add a comment (top-level or reply). Notifies issue creator.
    /// </summary>
    public async Task<object> AddCommentAsync(int issueId, CreateCommentDto input, string? userId)
    {
        var commentType = Enum.TryParse<CommentType>(input.Type, true, out var ct) ? ct : CommentType.General;

        var comment = new IssueComment
        {
            IssueId = issueId,
            Content = input.Content,
            StructuredMetadata = input.StructuredMetadata,
            IsHelpful = false,
            CreatorUserId = userId,
            ParentCommentId = input.ParentCommentId,
            Type = commentType
        };

        _context.IssueComments.Add(comment);
        await _context.SaveChangesAsync();

        if (userId != null)
        {
            var cleanText = System.Text.RegularExpressions.Regex.Replace(comment.Content, "<[^>]*>", "");
            var preview = cleanText.Length > 60 ? cleanText[..60] + "..." : cleanText;
            await _activityService.LogActivityAsync(issueId, userId, "CommentAdded", 
                $"Added comment: \"{preview}\"", new { commentId = comment.Id, commentType = comment.Type.ToString() });
        }

        // Notify issue creator about new comment
        var issue = await _context.Issues.FindAsync(issueId);
        if (issue?.CreatorUserId != null && issue.CreatorUserId != userId)
        {
            var issueCreator = await _userManager.FindByIdAsync(issue.CreatorUserId);
            if (issueCreator != null && !string.IsNullOrEmpty(issueCreator.Email))
            {
                var commenterName = await GetDisplayName(userId);
                try
                 {
                    await _emailService.SendEmailAsync(new MailRequest
                    {
                        To = issueCreator.Email,
                        Subject = $"[Feedback Hub] New comment on your issue #{issue.Id}",
                        Body = $"Hello {issueCreator.Name},\n\n{commenterName} commented on your issue '{issue.Title}'.\n\nView details: /app/issues/{issue.Id}"
                    });
                }
                catch { /* Email failures should not break comment flow */ }
            }
        }

        var creatorName = await GetDisplayName(userId);
        return new
        {
            id = comment.Id,
            content = comment.Content,
            creatorUserId = userId,
            creatorName,
            createdAt = comment.CreatedAt,
            parentCommentId = comment.ParentCommentId,
            score = 0,
            type = comment.Type.ToString(),
            userVote = 0,
            isHelpful = false,
            isRootCause = false,
            isReproConfirmed = false
        };
    }

    /// <summary>
    /// Edit a comment (only by the original author).
    /// </summary>
    public async Task<(bool Success, string Message, string? Content, DateTime? UpdatedAt)> EditCommentAsync(
        int commentId, string content, string userId)
    {
        var comment = await _context.IssueComments.FindAsync(commentId);
        if (comment == null)
            return (false, "Comment not found.", null, null);
        if (comment.CreatorUserId != userId)
            return (false, "Only the comment author can edit this comment.", null, null);

        comment.Content = content;
        await _context.SaveChangesAsync();
        return (true, "Comment updated.", comment.Content, comment.UpdatedAt);
    }

    /// <summary>
    /// Delete a comment. If it has replies, soft delete (mark as deleted, hide from UI entirely).
    /// If no replies, hard delete the comment and its votes.
    /// </summary>
    public async Task<(bool Success, string Message)> DeleteCommentAsync(int commentId, string userId)
    {
        var comment = await _context.IssueComments.FindAsync(commentId);
        if (comment == null)
            return (false, "Comment not found.");
        if (comment.CreatorUserId != userId)
            return (false, "Only the comment author can delete this comment.");

        // Soft delete — mark as deleted; will be hidden entirely from UI
        comment.IsDeleted = true;
        comment.DeletedAt = DateTime.UtcNow;

        // Check if comment has replies — if no replies, also delete votes and clean up
        var hasReplies = await _context.IssueComments.AnyAsync(c => c.ParentCommentId == commentId && !c.IsDeleted);
        if (!hasReplies)
        {
            var votes = await _context.CommentVotes.Where(v => v.CommentId == commentId).ToListAsync();
            _context.CommentVotes.RemoveRange(votes);
            _context.IssueComments.Remove(comment);
        }

        await _context.SaveChangesAsync();

        await _activityService.LogActivityAsync(comment.IssueId, userId, "CommentDeleted", 
            $"Deleted comment #{commentId}", new { commentId });

        return (true, "Comment deleted.");
    }

    /// <summary>
    /// Vote on a comment. Self-vote prevention. Returns (success, message, score, userVote).
    /// </summary>
    public async Task<(bool Success, string Message, int Score, int UserVote)> VoteCommentAsync(
        int commentId, int value, string userId)
    {
        var comment = await _context.IssueComments.FindAsync(commentId);
        if (comment == null)
            return (false, "Comment not found.", 0, 0);

        if (comment.CreatorUserId == userId)
            return (false, "Cannot vote on your own comment.", comment.Score, 0);

        var existing = await _context.CommentVotes
            .FirstOrDefaultAsync(v => v.CommentId == commentId && v.UserId == userId);

        int newUserVote;

        if (existing != null)
        {
            if (value == 0)
            {
                _context.CommentVotes.Remove(existing);
                comment.Score -= existing.Value;
                newUserVote = 0;
            }
            else if (existing.Value == value)
            {
                return (true, "Already voted this way.", comment.Score, existing.Value);
            }
            else
            {
                comment.Score -= existing.Value;
                existing.Value = value;
                comment.Score += value;
                newUserVote = value;
            }
        }
        else if (value != 0)
        {
            _context.CommentVotes.Add(new CommentVote { CommentId = commentId, UserId = userId, Value = value });
            comment.Score += value;
            newUserVote = value;
        }
        else
        {
            newUserVote = 0;
        }

        await _context.SaveChangesAsync();
        return (true, "Vote updated.", comment.Score, newUserVote);
    }

    /// <summary>
    /// Mark a comment as helpful (solution/workaround) or toggle it.
    /// Only the issue creator or an administrator can mark a comment as helpful.
    /// </summary>
    public async Task<(bool Success, string Message)> ToggleHelpfulAsync(int commentId, string userId, bool isAdmin)
    {
        var comment = await _context.IssueComments
            .Include(c => c.Issue)
            .FirstOrDefaultAsync(c => c.Id == commentId);

        if (comment == null)
            return (false, "Comment not found.");

        if (comment.Issue.CreatorUserId != userId && !isAdmin)
            return (false, "Only the issue creator or an administrator can mark a comment as helpful.");

        comment.IsHelpful = !comment.IsHelpful;
        await _context.SaveChangesAsync();

        if (comment.CreatorUserId != null)
        {
            await _gamificationService.RecalculateKarma(comment.CreatorUserId);
        }

        return (true, $"Comment marked as {(comment.IsHelpful ? "helpful" : "not helpful")}.");
    }

    /// <summary>
    /// Mark a comment as root cause or toggle it. Only administrators can do this.
    /// </summary>
    public async Task<(bool Success, string Message)> ToggleRootCauseAsync(int commentId, bool isAdmin)
    {
        if (!isAdmin)
            return (false, "Only administrators can mark a comment as root cause.");

        var comment = await _context.IssueComments.FindAsync(commentId);
        if (comment == null)
            return (false, "Comment not found.");

        comment.IsRootCause = !comment.IsRootCause;
        await _context.SaveChangesAsync();

        if (comment.CreatorUserId != null)
        {
            await _gamificationService.RecalculateKarma(comment.CreatorUserId);
        }

        return (true, $"Comment marked as {(comment.IsRootCause ? "root cause" : "not root cause")}.");
    }

    /// <summary>
    /// Mark a comment as repro confirmed or toggle it. Only administrators can do this.
    /// </summary>
    public async Task<(bool Success, string Message)> ToggleReproConfirmedAsync(int commentId, bool isAdmin)
    {
        if (!isAdmin)
            return (false, "Only administrators can mark a comment as repro confirmed.");

        var comment = await _context.IssueComments.FindAsync(commentId);
        if (comment == null)
            return (false, "Comment not found.");

        comment.IsReproConfirmed = !comment.IsReproConfirmed;
        await _context.SaveChangesAsync();

        if (comment.CreatorUserId != null)
        {
            await _gamificationService.RecalculateKarma(comment.CreatorUserId);
        }

        return (true, $"Comment marked as {(comment.IsReproConfirmed ? "repro confirmed" : "not repro confirmed")}.");
    }

    // --- Display name helpers ---

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
            result[id!] = await GetDisplayName(id);
        return result;
    }
}
