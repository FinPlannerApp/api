namespace Domain.Entities;

/// <summary>
/// Content definition for one day of the 30-Day Money Challenge.
/// This is shared content, not per-user — one row per day number, seeded once.
/// </summary>
public class ChallengeDay : BaseEntity
{
    /// <summary>1 through 30. Unique.</summary>
    public int DayNumber { get; set; }

    /// <summary>1 through 4 — which week this day belongs to.</summary>
    public int WeekNumber { get; set; }

    public required string Title { get; set; }
    public required string Description { get; set; }

    /// <summary>
    /// Rest days (7, 14, 21, 28) don't require a UserChallengeProgress row —
    /// they're shown in the timeline but excluded from completion-count math.
    /// </summary>
    public bool IsRestDay { get; set; }

    /// <summary>
    /// True for reflection/journaling days (8, 26, 29) where "completing"
    /// the day means writing something, not checking a box.
    /// </summary>
    public bool RequiresReflection { get; set; }

    /// <summary>
    /// Optional deep-link the frontend can use for a "Go do this" CTA button,
    /// e.g. "/accounts", "/budgets". Null for content-only days with nothing
    /// to link to in the app. Verify these match your actual Angular routes —
    /// they're my best guess based on your resource naming, not confirmed.
    /// </summary>
    public string? ActionRoute { get; set; }
}

/// <summary>
/// Tracks a single user's journey through the 30-day challenge: when they
/// started, their streak, and whether they've finished. One row per user
/// (auto-created on their first GetMyChallenge call — no explicit "start"
/// action required).
/// </summary>
public class UserChallengeEnrollment : BaseEntity
{
    public required string UserId { get; set; }

    public DateTime StartedAt { get; set; }

    /// <summary>Consecutive days with at least one completed challenge day.</summary>
    public int CurrentStreak { get; set; }
    public int LongestStreak { get; set; }

    /// <summary>
    /// Date (UTC, date-only comparison) of the last day the user completed
    /// anything. Used to compute streak continuation vs reset.
    /// </summary>
    public DateTime? LastActivityDate { get; set; }

    /// <summary>
    /// Set once all non-rest-day ChallengeDays have IsCompleted=true.
    /// Triggers the completion badge award.
    /// </summary>
    public DateTime? CompletedAt { get; set; }
}

/// <summary>
/// A single user's completion state for a single ChallengeDay.
/// Only exists once the user interacts with that day — no row means
/// "not started yet", not "incomplete".
/// </summary>
public class UserChallengeProgress : BaseEntity
{
    public required string UserId { get; set; }

    public int ChallengeDayId { get; set; }
    public ChallengeDay? ChallengeDay { get; set; }

    public bool IsCompleted { get; set; }
    public DateTime? CompletedAt { get; set; }

    /// <summary>
    /// Free-text reflection. Required (by convention, not DB constraint) for
    /// RequiresReflection days; optional notes on any other day if the user
    /// wants to jot something down.
    /// </summary>
    public string? ReflectionText { get; set; }
}
