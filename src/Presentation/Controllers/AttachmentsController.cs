using Application.Contracts;
using Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace Presentation.Controllers;

[Authorize]
[ApiController]
[Route("api/issues/{issueId}/attachments")]
public class AttachmentsController : ControllerBase
{
    private readonly IApplicationDbContext _context;

    public AttachmentsController(IApplicationDbContext context)
    {
        _context = context;
    }

    [HttpPost]
    [Authorize]
    public async Task<ActionResult> UploadAttachment(int issueId, IFormFile file)
    {
        var issue = await _context.Issues.FindAsync(issueId);
        if (issue == null) return NotFound();

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId == null) return Unauthorized();

        var uploadsPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "issues", issueId.ToString());
        Directory.CreateDirectory(uploadsPath);

        var safeFileName = Path.GetFileName(file.FileName);
        var filePath = Path.Combine(uploadsPath, safeFileName);
        using (var stream = new FileStream(filePath, FileMode.Create))
        {
            await file.CopyToAsync(stream);
        }

        var attachment = new IssueAttachment
        {
            IssueId = issueId,
            FileName = safeFileName,
            FilePath = $"/uploads/issues/{issueId}/{safeFileName}",
            ContentType = file.ContentType,
            FileSizeBytes = file.Length,
            UploadedByUserId = userId
        };

        _context.IssueAttachments.Add(attachment);
        await _context.SaveChangesAsync();

        return Ok(attachment);
    }

    [HttpGet]
    [AllowAnonymous]
    public async Task<ActionResult> GetAttachments(int issueId)
    {
        var attachments = await _context.IssueAttachments.Where(a => a.IssueId == issueId).ToListAsync();
        return Ok(attachments);
    }
}
