namespace Application.DTOs.Attachments;

public class AttachmentDto
{
    public int Id { get; set; }
    public string EntityType { get; set; } = "";
    public int EntityId { get; set; }
    public int Provider { get; set; }
    public string Url { get; set; } = "";
    public string Host { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string? MimeType { get; set; }
    public long? SizeBytes { get; set; }
    public bool AddedByMe { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class AddAttachmentDto
{
    public string EntityType { get; set; } = "";
    public int EntityId { get; set; }
    public int Provider { get; set; }          // 0 link, 1 Google Drive file
    public string? Url { get; set; }           // for links
    public string? ExternalId { get; set; }    // for Drive files
    public string DisplayName { get; set; } = "";
    public string? MimeType { get; set; }
    public long? SizeBytes { get; set; }
}
