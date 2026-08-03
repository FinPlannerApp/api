using Application.Common.Models;
using Application.DTOs.Challenge;

namespace Application.Contracts;

public interface IChallengeService
{
    /// <summary>
    /// Returns the requesting user's full challenge state. Auto-enrolls
    /// (creates a UserChallengeEnrollment with StartedAt = now) on first call —
    /// there's no separate "start the challenge" endpoint.
    /// </summary>
    Task<Result<ChallengeOverviewDto>> GetMyChallengeAsync(string userId);

    /// <summary>
    /// Marks a day complete (or updates the reflection text on an already-complete day).
    /// Updates the enrollment's streak. If this completes the final actionable
    /// day, sets CompletedAt on the enrollment and awards the completion badge.
    /// </summary>
    Task<Result<ChallengeDayDto>> MarkDayCompleteAsync(string userId, MarkDayCompleteDto dto);

    /// <summary>
    /// Undoes a day's completion. Does NOT retroactively adjust streak —
    /// see README for why that's a deliberate v1 simplification.
    /// </summary>
    Task<Result<bool>> UnmarkDayAsync(string userId, UnmarkDayDto dto);
}
