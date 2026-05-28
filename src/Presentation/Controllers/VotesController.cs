using Application.Services;
using Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using Application.Contracts;

namespace Presentation.Controllers;

[Authorize]
[ApiController]
[Route("api/issues")]
public class VotesController : ControllerBase
{
    private readonly VoteService _voteService;
    private readonly IApplicationDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;

    public VotesController(VoteService voteService, IApplicationDbContext context, UserManager<ApplicationUser> userManager)
    {
        _voteService = voteService;
        _context = context;
        _userManager = userManager;
    }

    [HttpPost("{id}/vote")]
    [Authorize]
    public async Task<ActionResult> ToggleVote(int id, [FromBody] VoteDto input)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId == null) return Unauthorized();

        var (success, message, votes, painScore, userVote) = await _voteService.ToggleVoteAsync(id, input.Value, userId);
        return Ok(new { success, message, votes, painScore, userVote });
    }

    [HttpGet("{id}/voters")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult> GetIssueVoters(int id)
    {
        var issue = await _context.Issues.FindAsync(id);
        if (issue == null) return NotFound();

        var votes = await _voteService.GetVotersAsync(id);
        var userIds = votes.Select(v => v.UserId).Distinct().ToList();

        var users = await _userManager.Users.Where(u => userIds.Contains(u.Id)).ToListAsync();
        var userMap = users.ToDictionary(u => u.Id);

        var result = votes.Select(v => new
        {
            v.UserId,
            DisplayName = userMap.TryGetValue(v.UserId, out var u) ? (u.Name ?? u.UserName ?? v.UserId) : v.UserId,
            v.Value
        }).ToList();

        return Ok(result);
    }
}
