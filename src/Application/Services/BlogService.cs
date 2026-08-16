using Application.Common.Models;
using Application.Contracts;
using Application.DTOs.Blog;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Application.Services;

public class BlogService
{
    private readonly IApplicationDbContext _context;

    public BlogService(IApplicationDbContext context)
    {
        _context = context;
    }

    // ── Public reads — no auth required, only ever returns published posts ────

    public async Task<List<BlogPostSummaryDto>> GetPublishedAsync()
    {
        return await _context.BlogPosts
            .Where(p => p.IsPublished)
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
        var post = await _context.BlogPosts.FirstOrDefaultAsync(p => p.Slug == slug && p.IsPublished);
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

    // ── Admin-only writes ────────────────────────────────────────────────────

    public async Task<Result<List<BlogPostDto>>> GetAllForAdminAsync()
    {
        var posts = await _context.BlogPosts
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
