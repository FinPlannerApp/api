using Application.Services;
using Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Presentation.Controllers;

[Authorize]
[ApiController]
[Route("api/issues")]
public class IssueActivitiesController : ControllerBase
{
    private readonly IssueActivityService _activityService;
    private readonly UserManager<ApplicationUser> _userManager;

    public IssueActivitiesController(IssueActivityService activityService, UserManager<ApplicationUser> userManager)
    {
        _activityService = activityService;
        _userManager = userManager;
    }

    [HttpGet("{id}/activities")]
    [AllowAnonymous]
    public async Task<ActionResult> GetActivities(int id)
    {
        var activities = await _activityService.GetActivitiesAsync(id);

        // Resolve display names
        var userIds = activities.Select(a => a.UserId).Distinct().ToList();
        var userMap = new Dictionary<string, string>();
        foreach (var userId in userIds)
        {
            var user = await _userManager.FindByIdAsync(userId);
            userMap[userId] = user?.Name ?? user?.UserName ?? (userId.Length > 8 ? userId[..8] + "..." : userId);
        }

        var result = activities.Select(a => new
        {
            a.Id,
            a.IssueId,
            a.UserId,
            UserName = userMap.GetValueOrDefault(a.UserId, "System"),
            a.ActivityType,
            a.Description,
            a.Metadata,
            a.CreatedAt
        }).ToList();

        return Ok(result);
    }
}
