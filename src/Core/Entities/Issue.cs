namespace Domain.Entities;

public enum IssueType { Bug, Feature, Question }

public class Issue : BaseEntity
{
    public required string Title { get; set; }
    public required string Description { get; set; }
    
    // Status & Lifecycle
    public string Status { get; set; } = "New"; // Open, Closed + sub-statuses
    public string Priority { get; set; } = "Medium"; // Low, Medium, High
    public IssueType Type { get; set; } = IssueType.Bug; // Bug, Feature, Question
    public bool IsClosed { get; set; } // GitHub-style open/closed
    public DateTime? ClosedAt { get; set; }
    public string? ClosedByUserId { get; set; }
    
    // Taxonomy Links
    public int? CategoryId { get; set; }
    public IssueTaxonomy? Category { get; set; }
    
    public int? SubcategoryId { get; set; }
    public IssueTaxonomy? Subcategory { get; set; }

    // Ranking Logic Inputs
    public string Severity { get; set; } = "Minor"; // Minor, Major, Critical
    public bool ImpactsMoney { get; set; }
    public decimal? FinancialImpactAmount { get; set; }
    public string Frequency { get; set; } = "Rare"; // Rare, Frequent, Always
    
    public int TrustPenalty { get; set; } = 0;
    public int Votes { get; set; } = 0;
    
    public double PainScore { get; set; } = 0; // Computed ranking score

    // User Info
    public string? CreatorUserId { get; set; }
    
    // External Links
    public string? GitHubIssueUrl { get; set; }
    
    // SLA Tracking
    public DateTime? AcknowledgedAt { get; set; }
    public DateTime? ResolvedAt { get; set; }
    
    // Milestone
    public int? MilestoneId { get; set; }
    public IssueMilestone? Milestone { get; set; }
    
    // Collections
    public ICollection<IssueComment> Comments { get; set; } = new List<IssueComment>();
    public ICollection<IssueVote> IssueVotes { get; set; } = new List<IssueVote>();
    public ICollection<IssueLabelAssignment> Labels { get; set; } = new List<IssueLabelAssignment>();
    public ICollection<IssueAssignee> Assignees { get; set; } = new List<IssueAssignee>();
    public ICollection<IssueAttachment> Attachments { get; set; } = new List<IssueAttachment>();
    public ICollection<IssueStatusHistory> StatusHistory { get; set; } = new List<IssueStatusHistory>();
}
