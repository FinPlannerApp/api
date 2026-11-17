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
        value = Math.Clamp(value, -1, 1); // never trust the client's vote weight
        var issue = await _context.Issues.FindAsync(issueId);
        if (issue == null)
            return (false, "Issue not found.", 0, 0, 0);

        if (issue.CreatorUserId == userId)
            return (false, "You cannot vote on your own issue.", issue.Votes, issue.PainScore, 0);

        var existingVote = await _context.IssueVotes
            .FirstOrDefaultAsync(v => v.IssueId == issueId && v.UserId == userId);

        int newUserVote;
        int delta; // change in the net vote count

        if (existingVote != null)
        {
            if (value == 0) // Explicit unvote
            {
                _context.IssueVotes.Remove(existingVote);
                delta = -existingVote.Value;
                newUserVote = 0;
            }
            else if (existingVote.Value == value) // Same vote again = no change
            {
                return (true, "You already voted this way.", issue.Votes, issue.PainScore, existingVote.Value);
            }
            else // Switching vote direction
            {
                delta = value - existingVote.Value;
                existingVote.Value = value;
                newUserVote = value;
            }
        }
        else
        {
            if (value != 0)
            {
                _context.IssueVotes.Add(new IssueVote { IssueId = issueId, UserId = userId, Value = value });
                delta = value;
                newUserVote = value;
            }
            else
            {
                return (true, "No vote to remove.", issue.Votes, issue.PainScore, 0);
            }
        }

        await _context.SaveChangesAsync();

        // SEC-32: bump the counters in the database with one atomic UPDATE (Votes = Votes + n) instead of
        // read-modify-write on a loaded copy, so two people voting at the same moment can't overwrite each other.
        if (delta != 0)
        {
            await _context.Issues.Where(i => i.Id == issueId).ExecuteUpdateAsync(s => s
                .SetProperty(i => i.Votes, i => i.Votes + delta)
                .SetProperty(i => i.PainScore, i => i.PainScore + delta * 10));
        }
        var fresh = await _context.Issues.AsNoTracking().Where(i => i.Id == issueId)
            .Select(i => new { i.Votes, i.PainScore }).FirstAsync();

        // Recalculate karma for the issue creator
        if (issue.CreatorUserId != null)
            await _gamificationService.RecalculateKarma(issue.CreatorUserId);

        return (true, value == 0 ? "Vote removed." : "Vote recorded.", fresh.Votes, fresh.PainScore, newUserVote);
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
