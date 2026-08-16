namespace Application.DTOs.Blog;

public class BlogPostSummaryDto
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string? Excerpt { get; set; }
    public DateTime? PublishedAt { get; set; }
}

public class BlogPostDto : BlogPostSummaryDto
{
    public string ContentMarkdown { get; set; } = string.Empty;
    public bool IsPublished { get; set; }
}

public class UpsertBlogPostDto
{
    public int? Id { get; set; } // null = create
    public required string Title { get; set; }
    public required string Slug { get; set; }
    public required string ContentMarkdown { get; set; }
    public string? Excerpt { get; set; }
    public bool IsPublished { get; set; }
}

public class BlogImageUploadResultDto
{
    public int Id { get; set; }
    public string PublicUrl { get; set; } = string.Empty;
}
