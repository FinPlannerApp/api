using Application.Common.Models;
using Application.Contracts;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Application.Services;

public class MigrationResult
{
    public int ImagesMigrated { get; set; }
    public int PostsUpdated { get; set; }
    public List<string> MigratedUrls { get; set; } = new();
}

public class BlogImageMigrationService
{
    private readonly IApplicationDbContext _context;
    private readonly R2BlogImageStorage _r2Storage;

    public BlogImageMigrationService(IApplicationDbContext context, R2BlogImageStorage r2Storage)
    {
        _context = context;
        _r2Storage = r2Storage;
    }

    /// <summary>
    /// Migrates all database-stored blog images to Cloudflare R2 and updates all blog post markdown references.
    /// </summary>
    public async Task<Result<MigrationResult>> MigrateAllImagesToR2Async()
    {
        try
        {
            var images = await _context.BlogImages.ToListAsync();
            var posts = await _context.BlogPosts.ToListAsync();

            var result = new MigrationResult();
            var urlReplacements = new Dictionary<string, string>(); // old relative or full URL -> new R2 URL

            foreach (var img in images)
            {
                var newR2Url = await _r2Storage.StoreAsync(img.Data, img.FileName);
                
                var oldRelativeUrl = $"/api/Blog/images/{img.Id}";
                urlReplacements[oldRelativeUrl] = newR2Url;

                result.ImagesMigrated++;
                result.MigratedUrls.Add($"{oldRelativeUrl} => {newR2Url}");
            }

            int postsUpdated = 0;
            foreach (var post in posts)
            {
                if (string.IsNullOrEmpty(post.ContentMarkdown)) continue;

                bool modified = false;
                foreach (var kvp in urlReplacements)
                {
                    if (post.ContentMarkdown.Contains(kvp.Key))
                    {
                        post.ContentMarkdown = post.ContentMarkdown.Replace(kvp.Key, kvp.Value);
                        modified = true;
                    }
                }

                if (modified)
                {
                    postsUpdated++;
                }
            }

            result.PostsUpdated = postsUpdated;
            await _context.SaveChangesAsync();

            return Result.Success(result);
        }
        catch (Exception ex)
        {
            return Result.Failure<MigrationResult>(new Error("BlogImageMigration.Failed", ex.Message));
        }
    }
}
