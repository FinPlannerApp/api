using Application.Contracts;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Application.Services;

/// <summary>
/// Handles all issue vote logic: toggle vote, self-vote prevention, pain score updates, karma recalc.
/// </summary>
public class VoteService
{
    private readonly IApplicationDbContext _context;
    private readonly GamificationService _gamificationService;

    public VoteService(IApplicationDbContext context, GamificationService gamificationService)
    {
        _context = context;
        _gamificationService = gamificationService;
    }

    /// <summary>
    /// Toggle a vote on an issue. Returns (success, message, votes, painScore, userVote).
    /// </summary>
    public async Task<(bool Success, string Message, int Votes, double PainScore, int UserVote)> ToggleVoteAsync(
        int issueId, int value, string userId)
    {
        var issue = await _context.Issues.FindAsync(issueId);
        if (issue == null)
            return (false, "Issue not found.", 0, 0, 0);

        if (issue.CreatorUserId == userId)
            return (false, "You cannot vote on your own issue.", issue.Votes, issue.PainScore, 0);

        var existingVote = await _context.IssueVotes
            .FirstOrDefaultAsync(v => v.IssueId == issueId && v.UserId == userId);

        int newUserVote;

        if (existingVote != null)
        {
            if (value == 0) // Explicit unvote
            {
                _context.IssueVotes.Remove(existingVote);
                issue.Votes -= existingVote.Value;
                issue.PainScore -= existingVote.Value * 10;
                newUserVote = 0;
            }
            else if (existingVote.Value == value) // Same vote again = no change
            {
                return (true, "You already voted this way.", issue.Votes, issue.PainScore, existingVote.Value);
            }
            else // Switching vote direction
            {
                issue.Votes -= existingVote.Value;
                issue.PainScore -= existingVote.Value * 10;
                existingVote.Value = value;
                issue.Votes += value;
                issue.PainScore += value * 10;
                newUserVote = value;
            }
        }
        else
        {
            if (value != 0)
            {
                var vote = new IssueVote { IssueId = issueId, UserId = userId, Value = value };
                _context.IssueVotes.Add(vote);
                issue.Votes += value;
                issue.PainScore += value * 10;
                newUserVote = value;
            }
            else
            {
                return (true, "No vote to remove.", issue.Votes, issue.PainScore, 0);
            }
        }

        await _context.SaveChangesAsync();

        // Recalculate karma for the issue creator
        if (issue.CreatorUserId != null)
            await _gamificationService.RecalculateKarma(issue.CreatorUserId);

        return (true, value == 0 ? "Vote removed." : "Vote recorded.", issue.Votes, issue.PainScore, newUserVote);
    }

    /// <summary>
    /// Get all voters for an issue (admin feature).
    /// </summary>
    public async Task<List<IssueVote>> GetVotersAsync(int issueId)
    {
        return await _context.IssueVotes
            .Where(v => v.IssueId == issueId)
            .ToListAsync();
    }
}
