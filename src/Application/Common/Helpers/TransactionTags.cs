namespace Application.Common.Helpers;

/// <summary>Tags on a transaction: normalised on the way in, split on the way out.</summary>
public static class TransactionTags
{
    public const int MaxTags = 8;
    public const int MaxTagLength = 30;
    public const int MaxNotesLength = 500;

    /// <summary>Lower-cases, trims, collapses spaces, drops blanks and duplicates, caps the count and length.
    /// Returns null when nothing is left, otherwise "|a|b|".</summary>
    public static string? Pack(IEnumerable<string>? tags)
    {
        if (tags == null) return null;
        var clean = new List<string>();
        foreach (var raw in tags)
        {
            var t = string.Join(' ', (raw ?? string.Empty).Replace("|", " ").Trim().ToLowerInvariant()
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)).TrimStart('#');
            if (t.Length == 0) continue;
            if (t.Length > MaxTagLength) t = t[..MaxTagLength];
            if (!clean.Contains(t)) clean.Add(t);
            if (clean.Count == MaxTags) break;
        }
        return clean.Count == 0 ? null : "|" + string.Join('|', clean) + "|";
    }

    public static List<string> Unpack(string? packed) =>
        string.IsNullOrEmpty(packed)
            ? new List<string>()
            : packed.Split('|', StringSplitOptions.RemoveEmptyEntries).ToList();

    /// <summary>The LIKE pattern that matches one exact tag in the packed column.</summary>
    public static string Pattern(string tag)
    {
        var t = (Pack(new[] { tag })?.Trim('|') ?? string.Empty)
            .Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");   // escape LIKE wildcards
        return "%|" + t + "|%";
    }

    public static string? CleanNotes(string? notes)
    {
        var n = notes?.Trim();
        if (string.IsNullOrEmpty(n)) return null;
        return n.Length > MaxNotesLength ? n[..MaxNotesLength] : n;
    }
}
