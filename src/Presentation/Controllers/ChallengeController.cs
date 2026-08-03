using Application.Contracts;
using Application.DTOs.Challenge;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[Route("api/[controller]")]
public class ChallengeController : BaseController
{
    private readonly IChallengeService _challengeService;

    public ChallengeController(IChallengeService challengeService)
    {
        _challengeService = challengeService;
    }

    /// <summary>
    /// Returns the requesting user's full 30-day challenge state.
    /// Auto-enrolls on first call.
    /// </summary>
    [HttpGet("mine")]
    public async Task<IActionResult> GetMine()
    {
        var result = await _challengeService.GetMyChallengeAsync(UserId);
        return HandleResult(result);
    }

    [HttpPost("complete")]
    public async Task<IActionResult> MarkComplete([FromBody] MarkDayCompleteDto dto)
    {
        var result = await _challengeService.MarkDayCompleteAsync(UserId, dto);
        return HandleResult(result);
    }

    [HttpPost("uncomplete")]
    public async Task<IActionResult> Uncomplete([FromBody] UnmarkDayDto dto)
    {
        var result = await _challengeService.UnmarkDayAsync(UserId, dto);
        return HandleResult(result);
    }
}
