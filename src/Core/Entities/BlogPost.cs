namespace Domain.Entities;

public class BlogPost : BaseEntity
{
    public required string Title { get; set; }
    public required string Slug { get; set; }
    public required string ContentMarkdown { get; set; }
    public string? Excerpt { get; set; }
    public bool IsPublished { get; set; } = false;
    public DateTime? PublishedAt { get; set; }
}
