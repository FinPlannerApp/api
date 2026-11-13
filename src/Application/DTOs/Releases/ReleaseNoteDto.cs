namespace Application.DTOs.Releases;

public class ReleaseNoteItemDto
{
    public string Type { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
}

public class ReleaseNoteDto
{
    public int Id { get; set; }
    /// <summary>"app" or "server".</summary>
    public string Stream { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    /// <summary>"major", "minor" or "patch", from the version number.</summary>
    public string Level { get; set; } = string.Empty;
    public DateTime ReleasedOn { get; set; }
    public string? Summary { get; set; }
    public string? BlogSlug { get; set; }
    public List<ReleaseNoteItemDto> Items { get; set; } = new();
}
