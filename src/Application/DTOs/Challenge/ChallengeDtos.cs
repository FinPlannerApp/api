namespace Application.DTOs.Challenge;

/// <summary>
/// One day, merged with the requesting user's progress on it.
/// This is what the frontend renders as a single day card.
/// </summary>
public class ChallengeDayDto
{
    public int Id { get; set; }
    public int DayNumber { get; set; }
    public int WeekNumber { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsRestDay { get; set; }
    public bool RequiresReflection { get; set; }
    public string? ActionRoute { get; set; }

    // ── User's progress on this day (merged in) ─────────────────────────────
    public bool IsCompleted { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? ReflectionText { get; set; }
}

/// <summary>
/// The full "my challenge" view — overview stats + all 30 days.
/// No locking in v1: every day is visible and completable in any order.
/// CurrentDayNumber is a suggestion for the UI ("You're on Day 12"), not an
/// enforcement mechanism.
/// </summary>
public class ChallengeOverviewDto
{
    public DateTime StartedAt { get; set; }

    /// <summary>Suggested day based on days elapsed since StartedAt, clamped 1-30.</summary>
    public int CurrentDayNumber { get; set; }

    public int CurrentStreak { get; set; }
    public int LongestStreak { get; set; }

    /// <summary>Count of completed non-rest days.</summary>
    public int CompletedDaysCount { get; set; }

    /// <summary>Total actionable days (30 minus the 4 rest days = 26).</summary>
    public int TotalActionableDays { get; set; }

    public bool IsFullyCompleted { get; set; }
    public DateTime? CompletedAt { get; set; }

    public List<ChallengeDayDto> Days { get; set; } = new();
}

public class MarkDayCompleteDto
{
    public int ChallengeDayId { get; set; }

    /// <summary>Required by convention (not enforced server-side) for RequiresReflection days.</summary>
    public string? ReflectionText { get; set; }
}

public class UnmarkDayDto
{
    public int ChallengeDayId { get; set; }
}
