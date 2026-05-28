using Application.Contracts;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Application.Services;

/// <summary>
/// Handles emoji reactions on comments.
/// </summary>
public class ReactionService
{
    private readonly IApplicationDbContext _context;
    
    private static readonly string[] ValidEmojis = { "👍", "👎", "❤️", "🎉", "😄", "😕", "👀", "🚀" };

    public ReactionService(IApplicationDbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Toggle a reaction on a comment. Returns (success, action).
    /// </summary>
    public async Task<(bool Success, string Action, string? Error)> ToggleReactionAsync(
        int commentId, string emoji, string userId)
    {
        if (!ValidEmojis.Contains(emoji))
            return (false, "", "Invalid emoji.");

        var existing = await _context.CommentReactions
            .FirstOrDefaultAsync(r => r.CommentId == commentId && r.UserId == userId && r.Emoji == emoji);

        if (existing != null)
        {
            _context.CommentReactions.Remove(existing);
            await _context.SaveChangesAsync();
            return (true, "removed", null);
        }

        _context.CommentReactions.Add(new CommentReaction
        {
            CommentId = commentId,
            UserId = userId,
            Emoji = emoji
        });
        await _context.SaveChangesAsync();
        return (true, "added", null);
    }

    /// <summary>
    /// Get grouped reactions for a comment with current user's reaction state.
    /// </summary>
    public async Task<List<object>> GetReactionsAsync(int commentId, string? currentUserId)
    {
        var reactions = await _context.CommentReactions
            .Where(r => r.CommentId == commentId)
            .ToListAsync();

        return reactions
            .GroupBy(r => r.Emoji)
            .Select(g => (object)new
            {
                emoji = g.Key,
                count = g.Count(),
                reacted = currentUserId != null && g.Any(r => r.UserId == currentUserId)
            })
            .ToList();
    }
}
