namespace Application.DTOs.Issue;

/// <summary>A bug report sent from the quick report button: what happened, plus what the app already knows.</summary>
public class QuickReportDto
{
    /// <summary>What the user typed (3 to 4000 characters).</summary>
    public string? Description { get; set; }
    /// <summary>Minor | Major | Critical.</summary>
    public string? Severity { get; set; }
    public bool? ImpactsMoney { get; set; }
    /// <summary>JSON the app collected: page, viewport, app version, recent errors, API calls and clicks.</summary>
    public string? ContextJson { get; set; }
    /// <summary>Optional screenshot as a base64 data URL or raw base64 (JPEG or PNG, up to about 2 MB).</summary>
    public string? ScreenshotBase64 { get; set; }
}
