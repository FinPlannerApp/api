using Application.Contracts;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Application.Services;

public class GamificationService
{
    private readonly IApplicationDbContext _context;

    public GamificationService(IApplicationDbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Recalculate karma for a specific user based on:
    /// - Upvotes received on their issues (+5 each)
    /// - Downvotes received on their issues (-3 each)
    /// - Upvotes received on their comments (+2 each)
    /// - Number of issues created (+1 each)
    /// </summary>
    public async Task RecalculateKarma(string userId)
    {
        // Votes received on user's issues
        var issueIds = await _context.Issues.Where(i => i.CreatorUserId == userId).Select(i => i.Id).ToListAsync();
        var issueVotes = await _context.IssueVotes.Where(v => issueIds.Contains(v.IssueId)).ToListAsync();
        var issueUpvotes = issueVotes.Count(v => v.Value > 0);
        var issueDownvotes = issueVotes.Count(v => v.Value < 0);

        // Votes on user's comments
        var commentIds = await _context.IssueComments.Where(c => c.CreatorUserId == userId).Select(c => c.Id).ToListAsync();
        var commentVotes = await _context.CommentVotes.Where(v => commentIds.Contains(v.CommentId)).ToListAsync();
        var commentUpvotes = commentVotes.Count(v => v.Value > 0);

        // Number of issues created
        var issueCount = issueIds.Count;

        var karma = (issueUpvotes * 5) - (issueDownvotes * 3) + (commentUpvotes * 2) + issueCount;

        // Get or create profile
        var profile = await _context.UserGamificationProfiles.FindAsync(userId);
        if (profile == null)
        {
            profile = new UserGamificationProfile { UserId = userId, KarmaScore = karma };
            _context.UserGamificationProfiles.Add(profile);
        }
        else
        {
            profile.KarmaScore = karma;
        }

        // Assign contributor tag
        profile.ContributorTag = karma switch
        {
            >= 100 => "Gold Reporter",
            >= 50 => "Silver Reporter",
            >= 20 => "Bronze Reporter",
            >= 5 => "Active Contributor",
            _ => null
        };

        await _context.SaveChangesAsync();

        // Check and award badges
        await CheckAndAwardBadges(userId, issueCount, karma);
    }

    private async Task CheckAndAwardBadges(string userId, int issueCount, int karma)
    {
        var existingBadgeIds = await _context.UserBadges.Where(ub => ub.UserId == userId).Select(ub => ub.BadgeId).ToListAsync();

        // Ensure seed badges exist
        var badges = await _context.Badges.ToListAsync();
        if (!badges.Any())
        {
            var seedBadges = new[]
            {
                new Badge { Name = "First Report", Description = "Submitted your first issue", IconUrl = "pi pi-flag", Color = "#10b981" },
                new Badge { Name = "Bug Hunter", Description = "Reported 5 or more bugs", IconUrl = "pi pi-search", Color = "#ef4444" },
                new Badge { Name = "Top Reporter", Description = "Earned 50+ karma", IconUrl = "pi pi-star", Color = "#f59e0b" },
                new Badge { Name = "Helpful Commenter", Description = "Earned karma from comments", IconUrl = "pi pi-comments", Color = "#3b82f6" },
                new Badge { Name = "Legend", Description = "Reached 100+ karma", IconUrl = "pi pi-trophy", Color = "#8b5cf6" },
            };
            _context.Badges.AddRange(seedBadges);
            await _context.SaveChangesAsync();
            badges = await _context.Badges.ToListAsync();
        }

        var badgeLookup = badges.ToDictionary(b => b.Name, b => b.Id);

        async Task TryAward(string badgeName)
        {
            if (badgeLookup.TryGetValue(badgeName, out var badgeId) && !existingBadgeIds.Contains(badgeId))
            {
                _context.UserBadges.Add(new UserBadge { UserId = userId, BadgeId = badgeId });
            }
        }

        if (issueCount >= 1) await TryAward("First Report");
        if (issueCount >= 5) await TryAward("Bug Hunter");
        if (karma >= 50) await TryAward("Top Reporter");
        if (karma >= 100) await TryAward("Legend");

        await _context.SaveChangesAsync();
    }
}
