using System.Security.Claims;
using Application.DTOs.Blog;
using Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[Route("api/[controller]")]
public class BlogController : BaseController
{
    private readonly BlogService _blogService;
    private readonly BlogImageMigrationService _migrationService;

    public BlogController(BlogService blogService, BlogImageMigrationService migrationService)
    {
        _blogService = blogService;
        _migrationService = migrationService;
    }

    [HttpGet("published")]
    [AllowAnonymous]
    [ResponseCache(Duration = 300, Location = ResponseCacheLocation.Any)]
    public async Task<IActionResult> GetPublished()
        => Ok(await _blogService.GetPublishedAsync());

    [HttpGet("published/paged")]
    [AllowAnonymous]
    [ResponseCache(Duration = 180, Location = ResponseCacheLocation.Any, VaryByQueryKeys = new[] { "pageNumber", "pageSize", "search", "tag" })]
    public async Task<IActionResult> GetPublishedPaged(
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 6,
        [FromQuery] string? search = null,
        [FromQuery] string? tag = null)
        => Ok(await _blogService.GetPublishedPagedAsync(pageNumber, pageSize, search, tag));

    [HttpGet("published/{slug}/comments")]
    [AllowAnonymous]
    public async Task<IActionResult> GetComments(string slug)
        => Ok(await _blogService.GetCommentsAsync(slug));

    [HttpGet("published/{slug}")]
    [AllowAnonymous]
    [ResponseCache(Duration = 300, Location = ResponseCacheLocation.Any)]
    public async Task<IActionResult> GetBySlug(string slug)
        => HandleResult(await _blogService.GetBySlugAsync(slug));

    [HttpPost("comments")]
    [Authorize]
    public async Task<IActionResult> CreateComment([FromBody] CreateBlogCommentDto dto)
    {
        var userName = User.Identity?.Name ?? User.FindFirstValue(System.Security.Claims.ClaimTypes.Email) ?? "Member User";
        return HandleResult(await _blogService.CreateCommentAsync(UserId, userName, dto));
    }

    [HttpGet("images/{id}")]
    [AllowAnonymous]
    [ResponseCache(Duration = 31536000, Location = ResponseCacheLocation.Any)]
    public async Task<IActionResult> GetImage(int id)
    {
        var result = await _blogService.GetImageAsync(id);
        if (result.IsFailure) return NotFound();

        var (data, contentType) = result.Value;
        return File(data, contentType);
    }

    [HttpGet("admin/all")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> GetAllForAdmin()
        => HandleResult(await _blogService.GetAllForAdminAsync());

    [HttpPost("admin/upsert")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Upsert([FromBody] UpsertBlogPostDto dto)
        => HandleResult(await _blogService.UpsertAsync(dto));

    [HttpPost("admin/{id}/delete")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Delete(int id)
        => HandleResult(await _blogService.DeleteAsync(id));

    [HttpPost("admin/upload-image")]
    [Authorize(Roles = "Admin")]
    [RequestSizeLimit(10_000_000)] // 10MB cap on uploaded source file before WebP conversion
    public async Task<IActionResult> UploadImage(IFormFile file)
    {
        if (file == null || file.Length == 0)
            return BadRequest("No file provided.");

        using var stream = file.OpenReadStream();
        var result = await _blogService.UploadImageAsync(stream, file.FileName);
        return HandleResult(result);
    }

    [HttpPost("admin/migrate-to-r2")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> MigrateToR2()
    {
        var result = await _migrationService.MigrateAllImagesToR2Async();
        return HandleResult(result);
    }
}
