namespace Domain.Entities;

/// <summary>The screenshot and technical context a user attached to a quick bug report (one per report).</summary>
public class IssueDiagnostic : BaseEntity
{
    public int IssueId { get; set; }
    public required string UserId { get; set; }
    /// <summary>JSON: page, viewport, app version, recent console errors, recent API calls, recent clicks.</summary>
    public required string ContextJson { get; set; }
    public byte[]? Screenshot { get; set; }
    public string? ScreenshotContentType { get; set; }
}
