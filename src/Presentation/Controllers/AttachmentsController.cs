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

    private const long MaxFileBytes = 5 * 1024 * 1024; // 5 MB

    // Allowlist only. Anything that a browser could execute from our own origin
    // (.html, .svg, .js, .xml, .exe …) is deliberately absent.
    private static readonly Dictionary<string, string[]> AllowedTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".png"]  = new[] { "image/png" },
        [".jpg"]  = new[] { "image/jpeg" },
        [".jpeg"] = new[] { "image/jpeg" },
        [".gif"]  = new[] { "image/gif" },
        [".webp"] = new[] { "image/webp" },
        [".pdf"]  = new[] { "application/pdf" },
        [".txt"]  = new[] { "text/plain" },
        [".log"]  = new[] { "text/plain", "application/octet-stream" },
    };

    // First bytes of each binary format, so a renamed file is rejected.
    private static bool HasExpectedSignature(string ext, byte[] head) => ext.ToLowerInvariant() switch
    {
        ".png"  => head.Length >= 8 && head[0] == 0x89 && head[1] == 0x50 && head[2] == 0x4E && head[3] == 0x47,
        ".jpg" or ".jpeg" => head.Length >= 3 && head[0] == 0xFF && head[1] == 0xD8 && head[2] == 0xFF,
        ".gif"  => head.Length >= 4 && head[0] == 0x47 && head[1] == 0x49 && head[2] == 0x46 && head[3] == 0x38,
        ".webp" => head.Length >= 12 && head[0] == 0x52 && head[1] == 0x49 && head[2] == 0x46 && head[3] == 0x46
                                      && head[8] == 0x57 && head[9] == 0x45 && head[10] == 0x42 && head[11] == 0x50,
        ".pdf"  => head.Length >= 4 && head[0] == 0x25 && head[1] == 0x50 && head[2] == 0x44 && head[3] == 0x46,
        _ => true // text formats have no signature
    };

    [HttpPost]
    [Authorize]
    [RequestSizeLimit(MaxFileBytes + 1024 * 64)]
    public async Task<ActionResult> UploadAttachment(int issueId, IFormFile? file)
    {
        var issue = await _context.Issues.FindAsync(issueId);
        if (issue == null) return NotFound();

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId == null) return Unauthorized();

        if (file == null || file.Length == 0)
            return BadRequest(new { message = "No file was uploaded." });
        if (file.Length > MaxFileBytes)
            return BadRequest(new { message = "File is too large (max 5 MB)." });

        var originalName = Path.GetFileName(file.FileName ?? string.Empty);
        var ext = Path.GetExtension(originalName);
        if (string.IsNullOrEmpty(ext) || !AllowedTypes.TryGetValue(ext, out var allowedContentTypes))
            return BadRequest(new { message = "This file type is not allowed." });
        if (!allowedContentTypes.Contains(file.ContentType ?? string.Empty, StringComparer.OrdinalIgnoreCase))
            return BadRequest(new { message = "File content type does not match its extension." });

        var head = new byte[12];
        await using (var peek = file.OpenReadStream())
        {
            var read = await peek.ReadAsync(head.AsMemory(0, head.Length));
            if (!HasExpectedSignature(ext, head[..read]))
                return BadRequest(new { message = "File contents do not match its extension." });
        }

        var uploadsPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "issues", issueId.ToString());
        Directory.CreateDirectory(uploadsPath);

        // Server-generated name: no overwriting someone else's file, no odd characters in a path.
        var storedName = $"{Guid.NewGuid():N}{ext.ToLowerInvariant()}";
        var filePath = Path.Combine(uploadsPath, storedName);
        await using (var stream = new FileStream(filePath, FileMode.CreateNew))
        {
            await file.CopyToAsync(stream);
        }

        var attachment = new IssueAttachment
        {
            IssueId = issueId,
            FileName = originalName.Length > 200 ? originalName[..200] : originalName, // display only
            FilePath = $"/uploads/issues/{issueId}/{storedName}",
            ContentType = file.ContentType!,
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
