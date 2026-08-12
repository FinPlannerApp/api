namespace Application.Common.Helpers;

/// <summary>
/// FinPlanner stores all dates as UTC (timestamptz in Postgres), which is
/// correct for storage — but "which month/week does this transaction belong
/// to" is inherently a LOCAL-timezone question, not a UTC one. Bucketing by
/// raw t.Date.Month (extracted directly from the UTC value) causes exactly
/// the bug reported: a transaction saved at local midnight on the 1st of a
/// month, in any timezone ahead of UTC, converts to the previous UTC day —
/// so ".Month" reads as the PREVIOUS month even though the user experienced
/// it as the 1st.
///
/// Fix pattern used everywhere this matters: compute period boundaries
/// (month start/end, week start/end, year start/end) in LOCAL time first,
/// then convert those few boundary values to UTC for the actual DB range
/// query. This is far cheaper and safer than converting every transaction
/// row — only 2 values need converting per query, not every row, and range
/// comparisons (>= / <=) remain simple, index-friendly, correctly-translated
/// SQL rather than needing per-row timezone math at the database level.
///
/// Hard-coded to Asia/Kolkata (IST) for now — FinPlanner is currently
/// single-region. If/when multi-region or per-user timezone support is
/// needed, this becomes the one place to generalize from.
///
/// IMPORTANT: "Asia/Kolkata" is the IANA identifier, required for this to
/// work on Linux (Docker/Render). The Windows-style ID "India Standard
/// Time" will throw a TimeZoneNotFoundException on Linux — don't swap it
/// in even though it looks more "correct" on Windows dev machines.
/// </summary>
public static class AppTimeZone
{
    public static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata");

    /// <summary>UTC-stored DateTime → local (IST) wall-clock time, for reading/bucketing.</summary>
    public static DateTime ToLocal(this DateTime utc)
    {
        var utcKind = utc.Kind == DateTimeKind.Utc ? utc : DateTime.SpecifyKind(utc, DateTimeKind.Utc);
        return TimeZoneInfo.ConvertTimeFromUtc(utcKind, Zone);
    }

    /// <summary>Local (IST) wall-clock boundary → UTC, for DB range queries.</summary>
    public static DateTime ToUtc(this DateTime local)
    {
        var unspecified = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(unspecified, Zone);
    }

    /// <summary>
    /// Local-time month boundaries [start, end] for the month containing utcInstant,
    /// returned already converted to UTC and ready for direct DB range comparison.
    /// </summary>
    public static (DateTime StartUtc, DateTime EndUtc) MonthBoundsUtc(DateTime utcInstant)
    {
        var local = utcInstant.ToLocal();
        var localStart = new DateTime(local.Year, local.Month, 1, 0, 0, 0, DateTimeKind.Unspecified);
        var localEnd = localStart.AddMonths(1).AddTicks(-1);
        return (localStart.ToUtc(), localEnd.ToUtc());
    }

    /// <summary>Same idea, for the calendar year.</summary>
    public static (DateTime StartUtc, DateTime EndUtc) YearBoundsUtc(DateTime utcInstant)
    {
        var local = utcInstant.ToLocal();
        var localStart = new DateTime(local.Year, 1, 1, 0, 0, 0, DateTimeKind.Unspecified);
        var localEnd = localStart.AddYears(1).AddTicks(-1);
        return (localStart.ToUtc(), localEnd.ToUtc());
    }

    /// <summary>Same idea, for the ISO-8601 week (Monday–Sunday) containing utcInstant.</summary>
    public static (DateTime StartUtc, DateTime EndUtc) WeekBoundsUtc(DateTime utcInstant)
    {
        var local = utcInstant.ToLocal();
        int isoDayOfWeek = ((int)local.DayOfWeek + 6) % 7; // Mon=0 .. Sun=6
        var localStart = local.Date.AddDays(-isoDayOfWeek);
        var localEnd = localStart.AddDays(7).AddTicks(-1);
        return (
            DateTime.SpecifyKind(localStart, DateTimeKind.Unspecified).ToUtc(),
            DateTime.SpecifyKind(localEnd, DateTimeKind.Unspecified).ToUtc()
        );
    }

    /// <summary>
    /// "Today," correctly meaning the current calendar day in the app's
    /// local timezone — NOT DateTime.UtcNow.Date, which answers a
    /// different question (today in UTC) that happens to match local
    /// time only some of the day, depending on the UTC offset. Use this
    /// anywhere code currently reaches for DateTime.UtcNow.Date to
    /// compare against a date the user thinks of as "today" — elapsed-day
    /// counts, "is this due today" checks, and similar.
    /// </summary>
    public static DateTime TodayLocal() => DateTime.UtcNow.ToLocal().Date;
}
