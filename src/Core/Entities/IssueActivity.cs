namespace Domain.Entities;

public class IssueActivity : BaseEntity
{
    public int IssueId { get; set; }
    public Issue Issue { get; set; } = null!;

    public required string UserId { get; set; }
    
    public required string ActivityType { get; set; } // e.g. Created, StatusChanged, CommentAdded, RelationAdded, AssigneeAdded, etc.
    public required string Description { get; set; }  // Human readable description
    
    public string? Metadata { get; set; } // JSON serialized extra data
}
