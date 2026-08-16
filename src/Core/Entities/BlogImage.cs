namespace Domain.Entities;

public class BlogImage : BaseEntity
{
    public required string FileName { get; set; }
    public required string ContentType { get; set; } = "image/webp";
    public required byte[] Data { get; set; }
    public long FileSize { get; set; }
}
