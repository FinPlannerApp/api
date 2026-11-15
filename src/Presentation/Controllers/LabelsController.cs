using Application.Contracts;
using Application.DTOs.Issue;
using Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace Presentation.Controllers;

[Authorize]
[ApiController]
[Route("api/issues")]
[EnableRateLimiting("api")]
public class LabelsController : ControllerBase
{
    private readonly IApplicationDbContext _context;

    public LabelsController(IApplicationDbContext context)
    {
        _context = context;
    }

    /// <summary>Only the issue's creator or an admin may change its labels.</summary>
    private async Task<bool> CanEditIssueAsync(int issueId)
    {
        if (User.IsInRole("Admin")) return true;
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return userId != null && await _context.Issues.AnyAsync(i => i.Id == issueId && i.CreatorUserId == userId);
    }

    [HttpGet("labels")]
    [AllowAnonymous]
    public async Task<ActionResult> GetLabels()
    {
        var labels = await _context.IssueLabels.OrderBy(l => l.Name).ToListAsync();
        return Ok(labels.Select(l => new IssueLabelDto { Id = l.Id, Name = l.Name, Color = l.Color, Description = l.Description }));
    }

    [HttpPost("labels")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult> CreateLabel([FromBody] CreateLabelDto input)
    {
        if (string.IsNullOrWhiteSpace(input.Name) || input.Name.Length > 40)
            return BadRequest(new { message = "Label name must be 1-40 characters." });
        if (!System.Text.RegularExpressions.Regex.IsMatch(input.Color ?? "", "^#[0-9a-fA-F]{6}$"))
            return BadRequest(new { message = "Color must be a hex value like #3b82f6." });
        var label = new IssueLabel { Name = input.Name.Trim(), Color = input.Color!, Description = input.Description?.Length > 200 ? input.Description[..200] : input.Description };
        _context.IssueLabels.Add(label);
        await _context.SaveChangesAsync();
        return Ok(new IssueLabelDto { Id = label.Id, Name = label.Name, Color = label.Color, Description = label.Description });
    }

    [HttpPost("{id}/labels/{labelId}")]
    [Authorize]
    public async Task<ActionResult> AddLabel(int id, int labelId)
    {
        if (!await CanEditIssueAsync(id)) return Forbid();
        if (!await _context.IssueLabels.AnyAsync(l => l.Id == labelId)) return NotFound();
        var exists = await _context.IssueLabelAssignments.AnyAsync(la => la.IssueId == id && la.LabelId == labelId);
        if (exists) return Ok(new { success = true, message = "Label already assigned." });
        _context.IssueLabelAssignments.Add(new IssueLabelAssignment { IssueId = id, LabelId = labelId });
        await _context.SaveChangesAsync();
        return Ok(new { success = true, message = "Label added." });
    }

    [HttpDelete("{id}/labels/{labelId}")]
    [Authorize]
    public async Task<ActionResult> RemoveLabel(int id, int labelId)
    {
        if (!await CanEditIssueAsync(id)) return Forbid();
        var assignment = await _context.IssueLabelAssignments.FirstOrDefaultAsync(la => la.IssueId == id && la.LabelId == labelId);
        if (assignment == null) return NotFound();
        _context.IssueLabelAssignments.Remove(assignment);
        await _context.SaveChangesAsync();
        return Ok(new { success = true, message = "Label removed." });
    }
}
