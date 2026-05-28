using Application.Contracts;
using Application.DTOs.Issue;
using Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Presentation.Controllers;

[Authorize]
[ApiController]
[Route("api/issues")]
public class LabelsController : ControllerBase
{
    private readonly IApplicationDbContext _context;

    public LabelsController(IApplicationDbContext context)
    {
        _context = context;
    }

    [HttpGet("labels")]
    [AllowAnonymous]
    public async Task<ActionResult> GetLabels()
    {
        var labels = await _context.IssueLabels.OrderBy(l => l.Name).ToListAsync();
        return Ok(labels.Select(l => new IssueLabelDto { Id = l.Id, Name = l.Name, Color = l.Color, Description = l.Description }));
    }

    [HttpPost("labels")]
    [Authorize]
    public async Task<ActionResult> CreateLabel([FromBody] CreateLabelDto input)
    {
        var label = new IssueLabel { Name = input.Name, Color = input.Color, Description = input.Description };
        _context.IssueLabels.Add(label);
        await _context.SaveChangesAsync();
        return Ok(new IssueLabelDto { Id = label.Id, Name = label.Name, Color = label.Color, Description = label.Description });
    }

    [HttpPost("{id}/labels/{labelId}")]
    [Authorize]
    public async Task<ActionResult> AddLabel(int id, int labelId)
    {
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
        var assignment = await _context.IssueLabelAssignments.FirstOrDefaultAsync(la => la.IssueId == id && la.LabelId == labelId);
        if (assignment == null) return NotFound();
        _context.IssueLabelAssignments.Remove(assignment);
        await _context.SaveChangesAsync();
        return Ok(new { success = true, message = "Label removed." });
    }
}
