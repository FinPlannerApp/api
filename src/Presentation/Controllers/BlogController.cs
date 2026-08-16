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
    private readonly BlogImageService _imageService;

    public BlogController(BlogService blogService, BlogImageService imageService)
    {
        _blogService = blogService;
        _imageService = imageService;
    }

    [HttpGet("published")]
    [AllowAnonymous]
    public async Task<IActionResult> GetPublished()
        => Ok(await _blogService.GetPublishedAsync());

    [HttpGet("published/{slug}")]
    [AllowAnonymous]
    public async Task<IActionResult> GetBySlug(string slug)
        => HandleResult(await _blogService.GetBySlugAsync(slug));

    [HttpGet("images/{id}")]
    [AllowAnonymous]
    [ResponseCache(Duration = 31536000, Location = ResponseCacheLocation.Any)]
    public async Task<IActionResult> GetImage(int id)
    {
        var result = await _imageService.GetImageAsync(id);
        if (result.IsFailure) return NotFound();
        return File(result.Value.Data, result.Value.ContentType);
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
    public async Task<IActionResult> UploadImage(IFormFile file)
    {
        if (file == null || file.Length == 0)
            return BadRequest("File is empty.");

        using var stream = file.OpenReadStream();
        var result = await _imageService.UploadAndCompressAsync(file.FileName, stream);
        if (result.IsFailure) return HandleResult(result);

        return Ok(new BlogImageUploadResultDto
        {
            Id = result.Value.Id,
            PublicUrl = result.Value.PublicUrl
        });
    }
}
