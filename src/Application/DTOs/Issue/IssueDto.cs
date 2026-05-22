using Domain.Entities;

namespace Application.DTOs.Issue;

public class IssueDto
{
    public int Id { get; set; }
    public required string Title { get; set; }
    public required string Description { get; set; }
    public required string Status { get; set; }
    public string Type { get; set; } = "Bug"; // Bug, Feature, Question
    public bool IsClosed { get; set; }
    public DateTime? ClosedAt { get; set; }
    public string? ClosedByName { get; set; }
    public double PainScore { get; set; }
    
    public required string CategoryName { get; set; }
    public string? SubcategoryName { get; set; }
    
    public string? Severity { get; set; }
    public string? Frequency { get; set; }
    public bool ImpactsMoney { get; set; }
    public decimal? FinancialImpactAmount { get; set; }
    
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public int Votes { get; set; }
    public int CommentCount { get; set; }
    
    public string? CreatorId { get; set; }
    public string? CreatorName { get; set; }
    public int UserVote { get; set; } // 1, -1, or 0
    
    // Phase 2: Labels
    public List<IssueLabelDto> Labels { get; set; } = new();
    
    // Phase 2: Assignees
    public List<AssigneeDto> Assignees { get; set; } = new();
    
    // Phase 2: Milestone
    public int? MilestoneId { get; set; }
    public string? MilestoneTitle { get; set; }
    
    // Phase 2: GitHub Link
    public string? GitHubIssueUrl { get; set; }
}

public class IssueLabelDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Color { get; set; } = "#6366f1";
    public string? Description { get; set; }
}

public class AssigneeDto
{
    public string UserId { get; set; } = "";
    public string DisplayName { get; set; } = "";
}

public class MilestoneDto
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public DateTime? DueDate { get; set; }
    public bool IsClosed { get; set; }
    public int OpenCount { get; set; }
    public int ClosedCount { get; set; }
    public double Progress { get; set; } // 0-100%
}
