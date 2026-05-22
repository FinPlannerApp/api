namespace Domain.Entities;

/// <summary>
/// Colored labels for categorizing issues (like GitHub labels).
/// </summary>
public class IssueLabel : BaseEntity
{
    public required string Name { get; set; }
    public string Color { get; set; } = "#6366f1"; // Default indigo — hex color
    public string? Description { get; set; }
    
    // Join table
    public ICollection<IssueLabelAssignment> Issues { get; set; } = new List<IssueLabelAssignment>();
}

/// <summary>
/// Many-to-many join between Issues and Labels.
/// </summary>
public class IssueLabelAssignment
{
    public int IssueId { get; set; }
    public Issue? Issue { get; set; }
    
    public int LabelId { get; set; }
    public IssueLabel? Label { get; set; }
}

/// <summary>
/// Milestone for grouping issues (like GitHub milestones).
/// </summary>
public class IssueMilestone : BaseEntity
{
    public required string Title { get; set; }
    public string? Description { get; set; }
    public DateTime? DueDate { get; set; }
    public bool IsClosed { get; set; }
    
    public ICollection<Issue> Issues { get; set; } = new List<Issue>();
}

/// <summary>
/// Assignee join table — multiple users can be assigned to an issue.
/// </summary>
public class IssueAssignee
{
    public int IssueId { get; set; }
    public Issue? Issue { get; set; }
    
    public required string UserId { get; set; }
}

/// <summary>
/// Emoji reactions on comments (like GitHub reactions).
/// </summary>
public class CommentReaction
{
    public int CommentId { get; set; }
    public IssueComment? Comment { get; set; }
    
    public required string UserId { get; set; }
    public required string Emoji { get; set; } // 👍 ❤️ 🎉 😄 😕 👀
}

/// <summary>
/// Attachments for issues (Screenshots, logs, etc.)
/// </summary>
public class IssueAttachment : BaseEntity
{
    public int IssueId { get; set; }
    public Issue? Issue { get; set; }
    
    public required string FileName { get; set; }
    public required string FilePath { get; set; } // Path on disk or cloud storage URL
    public required string ContentType { get; set; }
    public long FileSizeBytes { get; set; }
    
    public required string UploadedByUserId { get; set; }
}

/// <summary>
/// Gamification profile for a user to track Karma.
/// </summary>
public class UserGamificationProfile
{
    public required string UserId { get; set; } // Key
    public int KarmaScore { get; set; } = 0;
    
    // Contributor Tag (e.g. Gold Reporter, Top Contributor) - could be derived, but good to store if overridden
    public string? ContributorTag { get; set; }
}

/// <summary>
/// Badges that can be awarded to users.
/// </summary>
public class Badge : BaseEntity
{
    public required string Name { get; set; }
    public required string Description { get; set; }
    public string? IconUrl { get; set; } // e.g. pi pi-star, or URL
    public string? Color { get; set; } // Hex color for the badge
}

/// <summary>
/// Badges awarded to specific users.
/// </summary>
public class UserBadge
{
    public required string UserId { get; set; }
    
    public int BadgeId { get; set; }
    public Badge? Badge { get; set; }
    
    public DateTime AwardedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Audit trail of status transitions for an issue.
/// </summary>
public class IssueStatusHistory : BaseEntity
{
    public int IssueId { get; set; }
    public Issue? Issue { get; set; }
    
    public required string OldStatus { get; set; }
    public required string NewStatus { get; set; }
    
    public string? ChangedByUserId { get; set; }
    public DateTime ChangedAt { get; set; } = DateTime.UtcNow;
}

