using Application.Common.Models;
using Application.Contracts;
using Application.DTOs.Blog;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using SkiaSharp;

namespace Application.Services;

public class BlogService
{
    private readonly IApplicationDbContext _context;
    private readonly IBlogImageStorage _imageStorage;

    public BlogService(IApplicationDbContext context, IBlogImageStorage imageStorage)
    {
        _context = context;
        _imageStorage = imageStorage;
    }

    // ── Public reads — no auth required, only ever returns published posts ────

    public async Task<PagedResult<BlogPostSummaryDto>> GetPublishedPagedAsync(int pageNumber = 1, int pageSize = 6, string? search = null, string? tag = null)
    {
        var query = _context.BlogPosts
            .AsNoTracking()
            .Where(p => p.IsPublished && !p.IsDeleted);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(p => p.Title.ToLower().Contains(term) || p.Excerpt.ToLower().Contains(term));
        }

        if (!string.IsNullOrWhiteSpace(tag) && !tag.Equals("All", StringComparison.OrdinalIgnoreCase))
        {
            var tagTerm = tag.Trim().ToLower();
            query = query.Where(p => p.Title.ToLower().Contains(tagTerm) || p.Slug.ToLower().Contains(tagTerm));
        }

        var totalCount = await query.CountAsync();
        var items = await query
            .OrderByDescending(p => p.PublishedAt ?? p.CreatedAt)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .Select(p => new BlogPostSummaryDto
            {
                Id = p.Id,
                Title = p.Title,
                Slug = p.Slug,
                Excerpt = p.Excerpt,
                PublishedAt = p.PublishedAt
            })
            .ToListAsync();

        return new PagedResult<BlogPostSummaryDto>(items, totalCount, pageNumber, pageSize);
    }

    public async Task<List<BlogPostSummaryDto>> GetPublishedAsync()
    {
        return await _context.BlogPosts
            .Where(p => p.IsPublished && !p.IsDeleted)
            .OrderByDescending(p => p.PublishedAt)
            .Select(p => new BlogPostSummaryDto
            {
                Id = p.Id,
                Title = p.Title,
                Slug = p.Slug,
                Excerpt = p.Excerpt,
                PublishedAt = p.PublishedAt
            })
            .ToListAsync();
    }

    public async Task<Result<BlogPostDto>> GetBySlugAsync(string slug)
    {
        var post = await _context.BlogPosts.FirstOrDefaultAsync(p => p.Slug == slug && !p.IsDeleted);
        if (post == null)
            return Result.Failure<BlogPostDto>(new Error("BlogPost.NotFound", "Post not found."));

        return Result.Success(new BlogPostDto
        {
            Id = post.Id,
            Title = post.Title,
            Slug = post.Slug,
            Excerpt = post.Excerpt,
            PublishedAt = post.PublishedAt,
            ContentMarkdown = post.ContentMarkdown,
            IsPublished = post.IsPublished
        });
    }

    public async Task<List<BlogCommentDto>> GetCommentsAsync(string slug)
    {
        var post = await _context.BlogPosts.FirstOrDefaultAsync(p => p.Slug == slug);
        if (post == null) return new List<BlogCommentDto>();

        var allComments = await _context.BlogPostComments
            .Where(c => c.BlogPostId == post.Id)
            .OrderByDescending(c => c.CreatedAt)
            .Select(c => new BlogCommentDto
            {
                Id = c.Id,
                Author = c.UserName,
                Content = c.Content,
                CreatedAt = c.CreatedAt.ToString("g"),
                Likes = c.LikesCount,
                ParentCommentId = c.ParentCommentId
            })
            .ToListAsync();

        var commentMap = allComments.ToDictionary(c => c.Id);
        var rootComments = new List<BlogCommentDto>();

        foreach (var c in allComments)
        {
            if (c.ParentCommentId.HasValue && commentMap.TryGetValue(c.ParentCommentId.Value, out var parent))
            {
                parent.Replies.Add(c);
            }
            else
            {
                rootComments.Add(c);
            }
        }

        return rootComments;
    }

    public async Task<Result<BlogCommentDto>> CreateCommentAsync(string userId, string userName, CreateBlogCommentDto dto)
    {
        var post = await _context.BlogPosts.FirstOrDefaultAsync(p => p.Slug == dto.PostSlug);
        if (post == null) return Result.Failure<BlogCommentDto>(new Error("BlogPost.NotFound", "Post not found."));

        var comment = new BlogPostComment
        {
            BlogPostId = post.Id,
            UserId = userId,
            UserName = userName,
            Content = dto.Content.Trim(),
            ParentCommentId = dto.ParentCommentId,
            CreatedAt = DateTime.UtcNow
        };

        _context.BlogPostComments.Add(comment);
        await _context.SaveChangesAsync();

        return Result.Success(new BlogCommentDto
        {
            Id = comment.Id,
            Author = comment.UserName,
            Content = comment.Content,
            CreatedAt = "Just now",
            Likes = 0,
            ParentCommentId = comment.ParentCommentId
        });
    }

    // ── Image Handling ────────────────────────────────────────────────────────

    public async Task<Result<string>> UploadImageAsync(Stream imageStream, string fileName)
    {
        // Converts whatever format was uploaded (PNG, JPEG, whatever)
        // into WebP — guarantees what's actually stored matches what
        // you decided on, regardless of what the browser sent.
        using var memoryStream = new MemoryStream();
        await imageStream.CopyToAsync(memoryStream);
        var inputBytes = memoryStream.ToArray();

        using var original = SKBitmap.Decode(inputBytes);
        if (original == null)
            return Result.Failure<string>(new Error("BlogImage.InvalidImage", "Could not decode the uploaded file as an image."));

        using var skImage = SKImage.FromBitmap(original);
        using var encodedData = skImage.Encode(SKEncodedImageFormat.Webp, quality: 80);

        var publicUrl = await _imageStorage.StoreAsync(encodedData.ToArray(), fileName);
        return Result.Success(publicUrl);
    }

    public async Task<Result<(byte[] Data, string ContentType)>> GetImageAsync(int id)
    {
        var image = await _context.BlogImages.FirstOrDefaultAsync(i => i.Id == id);
        if (image == null)
            return Result.Failure<(byte[], string)>(new Error("BlogImage.NotFound", "Image not found."));

        return Result.Success((image.Data, image.ContentType));
    }

    // ── Admin-only writes ────────────────────────────────────────────────────

    public async Task<Result<List<BlogPostDto>>> GetAllForAdminAsync()
    {
        var posts = await _context.BlogPosts
            .Where(p => !p.IsDeleted)
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync();

        return Result.Success(posts.Select(p => new BlogPostDto
        {
            Id = p.Id,
            Title = p.Title,
            Slug = p.Slug,
            Excerpt = p.Excerpt,
            PublishedAt = p.PublishedAt,
            ContentMarkdown = p.ContentMarkdown,
            IsPublished = p.IsPublished
        }).ToList());
    }

    public async Task<Result<BlogPostDto>> UpsertAsync(UpsertBlogPostDto dto)
    {
        var slugTaken = await _context.BlogPosts
            .AnyAsync(p => p.Slug == dto.Slug && p.Id != dto.Id);
        if (slugTaken)
            return Result.Failure<BlogPostDto>(new Error("BlogPost.SlugTaken", "This slug is already used by another post."));

        BlogPost post;
        if (dto.Id.HasValue)
        {
            var existing = await _context.BlogPosts.FirstOrDefaultAsync(p => p.Id == dto.Id.Value);
            if (existing == null)
                return Result.Failure<BlogPostDto>(new Error("BlogPost.NotFound", "Post not found."));
            post = existing;
        }
        else
        {
            post = new BlogPost { Title = dto.Title, Slug = dto.Slug, ContentMarkdown = dto.ContentMarkdown };
            _context.BlogPosts.Add(post);
        }

        var wasPublished = post.IsPublished;
        post.Title = dto.Title;
        post.Slug = dto.Slug;
        post.ContentMarkdown = dto.ContentMarkdown;
        post.Excerpt = dto.Excerpt;
        post.IsPublished = dto.IsPublished;

        if (dto.IsPublished && !wasPublished && post.PublishedAt == null)
        {
            post.PublishedAt = DateTime.UtcNow;
        }

        await _context.SaveChangesAsync();

        return Result.Success(new BlogPostDto
        {
            Id = post.Id,
            Title = post.Title,
            Slug = post.Slug,
            Excerpt = post.Excerpt,
            PublishedAt = post.PublishedAt,
            ContentMarkdown = post.ContentMarkdown,
            IsPublished = post.IsPublished
        });
    }

    public async Task<Result<bool>> DeleteAsync(int id)
    {
        var post = await _context.BlogPosts.FirstOrDefaultAsync(p => p.Id == id);
        if (post == null)
            return Result.Failure<bool>(new Error("BlogPost.NotFound", "Post not found."));

        post.IsDeleted = true;
        post.DeletedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return Result.Success(true);
    }
}
