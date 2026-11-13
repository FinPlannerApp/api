using Application.Common.Models;
using Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

/// <summary>Release notes for the "What's new" window and the blog. Content comes from Flyway migrations.</summary>
[Route("api/[controller]")]
public class ReleasesController : BaseController
{
    private readonly ReleaseNotesService _releases;

    public ReleasesController(ReleaseNotesService releases) => _releases = releases;

    /// <summary>
    /// Latest releases, newest first. <c>afterId</c> = the newest release id the caller has already
    /// seen, so the response is only what is new to them. <c>count</c> caps the list (1 to 10).
    /// </summary>
    [HttpGet("recent")]
    [AllowAnonymous]
    [ResponseCache(Duration = 60, Location = ResponseCacheLocation.Any, VaryByQueryKeys = new[] { "afterId", "count" })]
    public async Task<IActionResult> GetRecent([FromQuery] int? afterId, [FromQuery] int count = 5)
        => HandleResult(Result.Success(await _releases.GetRecentAsync(afterId, count)));
}
