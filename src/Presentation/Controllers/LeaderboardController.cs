using Application.Contracts;
using Application.Services;
using Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace Presentation.Controllers;

[ApiController]
[Route("api/gamification")]
public class LeaderboardController : ControllerBase
{
    private readonly IApplicationDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly GamificationService _gamificationService;

    public LeaderboardController(
        IApplicationDbContext context,
        UserManager<ApplicationUser> userManager,
        GamificationService gamificationService)
    {
        _context = context;
        _userManager = userManager;
        _gamificationService = gamificationService;
    }

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

    [HttpGet("badges")]
    [AllowAnonymous]
    public async Task<ActionResult> GetAllBadges()
    {
        var badges = await _context.Badges.ToListAsync();
        return Ok(badges);
    }

    [HttpGet("profile")]
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
