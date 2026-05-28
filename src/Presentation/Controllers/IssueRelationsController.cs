using Application.Services;
using Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
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

    public IssueRelationsController(IssueRelationService relationService)
    {
        _relationService = relationService;
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

        var (success, error) = await _relationService.RemoveRelationAsync(id, targetIssueId, relType, userId);

        if (!success)
        {
            return BadRequest(new { message = error });
        }

        return Ok(new { success = true, message = "Relation removed successfully." });
    }
}
