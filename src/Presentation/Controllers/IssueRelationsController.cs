using Application.Contracts;
using Application.Services;
using Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace Presentation.Controllers;

public class CreateRelationDto
{
    public int TargetIssueId { get; set; }
    public required string RelationType { get; set; }
}

[Authorize]
[ApiController]
[Route("api/issues")]
public class IssueRelationsController : ControllerBase
{
    private readonly IssueRelationService _relationService;
    private readonly IApplicationDbContext _context;

    public IssueRelationsController(IssueRelationService relationService, IApplicationDbContext context)
    {
        _relationService = relationService;
        _context = context;
    }

    // Only the issue's creator or an admin may change its relations — previously any
    // signed-in user could rewire any issue.
    private async Task<bool> CanEditIssueAsync(int issueId, string userId)
    {
        if (User.IsInRole("Admin")) return true;
        return await _context.Issues.AnyAsync(i => i.Id == issueId && i.CreatorUserId == userId);
    }

    [HttpGet("{id}/relations")]
    [AllowAnonymous]
    public async Task<ActionResult> GetRelations(int id)
    {
        var relations = await _relationService.GetRelationsAsync(id);
        var result = relations.Select(r => new
        {
            r.Id,
            r.IssueId,
            r.TargetIssueId,
            TargetTitle = r.TargetIssue.Title,
            TargetStatus = r.TargetIssue.Status.ToString(),
            RelationType = r.RelationType.ToString()
        }).ToList();

        return Ok(result);
    }

    [HttpPost("{id}/relations")]
    public async Task<ActionResult> AddRelation(int id, [FromBody] CreateRelationDto input)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId == null) return Unauthorized();

        if (!Enum.TryParse<IssueRelationType>(input.RelationType, true, out var relType))
        {
            return BadRequest(new { message = "Invalid relation type." });
        }

        if (!await CanEditIssueAsync(id, userId)) return Forbid();

        var (success, error) = await _relationService.AddRelationAsync(id, input.TargetIssueId, relType, userId);

        if (!success)
        {
            return BadRequest(new { message = error });
        }

        return Ok(new { success = true, message = "Relation added successfully." });
    }

    [HttpDelete("{id}/relations/{targetIssueId}/{relationType}")]
    public async Task<ActionResult> RemoveRelation(int id, int targetIssueId, string relationType)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId == null) return Unauthorized();

        if (!Enum.TryParse<IssueRelationType>(relationType, true, out var relType))
        {
            return BadRequest(new { message = "Invalid relation type." });
        }

        if (!await CanEditIssueAsync(id, userId)) return Forbid();

        var (success, error) = await _relationService.RemoveRelationAsync(id, targetIssueId, relType, userId);

        if (!success)
        {
            return BadRequest(new { message = error });
        }

        return Ok(new { success = true, message = "Relation removed successfully." });
    }
}
