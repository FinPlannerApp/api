namespace Application.DTOs.Issue;

public class CreateIssueDto
{
    public required string Title { get; set; }
    public required string Description { get; set; }
    public string Priority { get; set; } = "Medium";
    public string Type { get; set; } = "Bug"; // Bug, Feature, Question
    
    public int? CategoryId { get; set; }
    public int? SubcategoryId { get; set; }
    
    public string Severity { get; set; } = "Minor";
    public bool ImpactsMoney { get; set; }
    public decimal? FinancialImpactAmount { get; set; }
    public string Frequency { get; set; } = "Rare";
    
    public string? GitHubIssueUrl { get; set; }
    
    // Initial comment or metadata?
    public string? StructuredExpecations { get; set; }
}
