namespace Domain.Entities;

/// <summary>
/// Deliberately its own table, not a column on BlogPost — keeps the
/// post table itself small and fast to query even as the image table
/// grows. When this migrates to R2 later, this table either gets
/// dropped entirely or kept temporarily as a migration source; either
/// way, BlogPost itself never needs to change.
/// </summary>
public class BlogImage : BaseEntity
{
    public required string FileName { get; set; }
    public required byte[] Data { get; set; }
    public required string ContentType { get; set; } = "image/webp"; // always "image/webp" via this storage implementation
    public long SizeBytes { get; set; }

    // Backward-compatibility alias
    public long FileSize
    {
        get => SizeBytes;
        set => SizeBytes = value;
    }
}
