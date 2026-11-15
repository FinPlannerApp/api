namespace Domain.Entities;

/// <summary>
/// A pointer to a document that lives somewhere else (the user's own Drive, Dropbox, OneDrive...). The database keeps
/// only the reference, never the file, so a policy PDF or a receipt costs a few hundred bytes.
/// </summary>
public class Attachment : BaseEntity
{
    public required string UserId { get; set; }           // who added it
    public required string EntityType { get; set; }       // InsurancePolicy | Transaction | SplitExpense
    public int EntityId { get; set; }
    public int Provider { get; set; }                     // AttachmentProvider
    public string? Url { get; set; }                      // validated https link (Provider = Link)
    public string? ExternalId { get; set; }               // Drive file id (Provider = GoogleDrive)
    public required string DisplayName { get; set; }
    public string? MimeType { get; set; }
    public long? SizeBytes { get; set; }
}

public enum AttachmentProvider { Link = 0, GoogleDrive = 1 }
