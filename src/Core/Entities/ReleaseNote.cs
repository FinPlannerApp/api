namespace Domain.Entities;

/// <summary>
/// One release of the app ("app", the UI) or of the backend ("server"), with the notes users read
/// in the "What's new" window and on the blog. Rows are written by Flyway migrations
/// (db/migrations/V__release_notes_*.sql), so the content lives in the database, not in the bundle.
/// Minor and major releases also have a blog article (<see cref="BlogSlug"/>); patch releases are
/// folded into the next minor/major article automatically.
/// </summary>
public class ReleaseNote
{
    public int Id { get; set; }
    /// <summary>"app" or "server".</summary>
    public required string Stream { get; set; }
    /// <summary>Semantic version, e.g. "6.2.0". Matches the number the release pipeline assigned.</summary>
    public required string Version { get; set; }
    public DateTime ReleasedOn { get; set; }
    public string? Summary { get; set; }
    /// <summary>Slug of the BlogPosts article for this release (minor and major releases).</summary>
    public string? BlogSlug { get; set; }
    public List<ReleaseNoteItem> Items { get; set; } = new();
}

public class ReleaseNoteItem
{
    public int Id { get; set; }
    public int ReleaseNoteId { get; set; }
    public int SortOrder { get; set; }
    /// <summary>"new", "improved" or "fixed".</summary>
    public required string Type { get; set; }
    public required string Text { get; set; }
}
