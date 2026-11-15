namespace Application.DTOs.Issue;

/// <summary>One of the signed-in person's own reports, with where it stands.</summary>
public class MyReportDto
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    /// <summary>New | Acknowledged | Triaged | Planned | InProgress | Released | Verified | Closed.</summary>
    public string Status { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
    public bool IsClosed { get; set; }
    public DateTime CreatedAt { get; set; }
    /// <summary>True when it was sent with the quick report button (it carries a screenshot and technical details).</summary>
    public bool HasDiagnostics { get; set; }
    public bool HasScreenshot { get; set; }
    /// <summary>How many other people sent a report with the same fingerprint (same page, error and failing call).</summary>
    public int SimilarReports { get; set; }
}
