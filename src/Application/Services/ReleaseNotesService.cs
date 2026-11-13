using System.Text;
using Application.Contracts;
using Application.DTOs.Releases;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Application.Services;

/// <summary>Semantic-version helpers for release notes.</summary>
public static class ReleaseVersion
{
    public static (int Major, int Minor, int Patch) Parse(string version)
    {
        var parts = version.Split('.');
        int Part(int i) => i < parts.Length && int.TryParse(parts[i], out var n) ? n : 0;
        return (Part(0), Part(1), Part(2));
    }

    public static int Compare(string a, string b) => Parse(a).CompareTo(Parse(b));

    /// <summary>"major" (X.0.0), "minor" (X.Y.0) or "patch" (X.Y.Z with Z &gt; 0).</summary>
    public static string Level(string version)
    {
        var (_, minor, patch) = Parse(version);
        return patch > 0 ? "patch" : minor > 0 ? "minor" : "major";
    }
}

public class ReleaseNotesService
{
    private const int MaxCount = 10;
    private readonly IApplicationDbContext _context;

    public ReleaseNotesService(IApplicationDbContext context) => _context = context;

    /// <summary>
    /// The newest releases for the "What's new" window. With <paramref name="afterId"/> it returns what
    /// the person has not seen yet (Id above the last one they saw); without it, the latest
    /// <paramref name="count"/> releases.
    /// </summary>
    public async Task<List<ReleaseNoteDto>> GetRecentAsync(int? afterId, int count)
    {
        var take = Math.Clamp(count, 1, MaxCount);

        var query = _context.ReleaseNotes.AsNoTracking().Include(r => r.Items).AsQueryable();
        if (afterId.HasValue)
            query = query.Where(r => r.Id > afterId.Value);

        var rows = await query
            .OrderByDescending(r => r.ReleasedOn).ThenByDescending(r => r.Id)
            .Take(take)
            .ToListAsync();

        return rows.Select(ToDto).ToList();
    }

    /// <summary>
    /// For a minor or major release's blog article: the patch releases shipped since the previous
    /// minor/major release of the same stream, as a markdown section. Empty for anything else.
    /// </summary>
    public async Task<string> BuildAccumulatedPatchesMarkdownAsync(string slug)
    {
        var release = await _context.ReleaseNotes.AsNoTracking().FirstOrDefaultAsync(r => r.BlogSlug == slug);
        if (release == null || ReleaseVersion.Level(release.Version) == "patch")
            return string.Empty;

        var sameStream = await _context.ReleaseNotes.AsNoTracking()
            .Include(r => r.Items)
            .Where(r => r.Stream == release.Stream)
            .ToListAsync();

        var previous = sameStream
            .Where(r => ReleaseVersion.Compare(r.Version, release.Version) < 0 && ReleaseVersion.Level(r.Version) != "patch")
            .OrderByDescending(r => ReleaseVersion.Parse(r.Version))
            .FirstOrDefault();
        if (previous == null) return string.Empty;

        var patches = sameStream
            .Where(r => ReleaseVersion.Level(r.Version) == "patch"
                        && ReleaseVersion.Compare(r.Version, previous.Version) > 0
                        && ReleaseVersion.Compare(r.Version, release.Version) < 0)
            .OrderBy(r => ReleaseVersion.Parse(r.Version))
            .ToList();
        if (patches.Count == 0 || patches.All(p => p.Items.Count == 0)) return string.Empty;

        var md = new StringBuilder();
        md.Append("\n\n---\n\n## Also fixed and improved since ").Append(previous.Version).Append("\n\n");
        foreach (var patch in patches)
        foreach (var item in patch.Items.OrderBy(i => i.SortOrder))
            md.Append("- ").Append(item.Text).Append(" *(").Append(patch.Version).Append(")*\n");
        return md.ToString();
    }

    private static ReleaseNoteDto ToDto(ReleaseNote r) => new()
    {
        Id = r.Id,
        Stream = r.Stream,
        Version = r.Version,
        Level = ReleaseVersion.Level(r.Version),
        ReleasedOn = r.ReleasedOn,
        Summary = r.Summary,
        BlogSlug = r.BlogSlug,
        Items = r.Items.OrderBy(i => i.SortOrder).Select(i => new ReleaseNoteItemDto { Type = i.Type, Text = i.Text }).ToList()
    };
}
