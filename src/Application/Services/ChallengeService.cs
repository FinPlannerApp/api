using Application.Common.Models;
using Application.Contracts;
using Application.DTOs.Challenge;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Application.Services;

public class ChallengeService : IChallengeService
{
    private readonly IApplicationDbContext _context;
    private const string CompletionBadgeName = "30-Day Money Challenge Graduate";

    public ChallengeService(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Result<ChallengeOverviewDto>> GetMyChallengeAsync(string userId)
    {
        var enrollment = await GetOrCreateEnrollmentAsync(userId);

        var days = await _context.ChallengeDays
            .OrderBy(d => d.DayNumber)
            .ToListAsync();

        var progress = await _context.UserChallengeProgresses
            .Where(p => p.UserId == userId)
            .ToListAsync();

        var progressByDayId = progress.ToDictionary(p => p.ChallengeDayId);

        var dayDtos = days.Select(d =>
        {
            progressByDayId.TryGetValue(d.Id, out var p);
            return new ChallengeDayDto
            {
                Id = d.Id,
                DayNumber = d.DayNumber,
                WeekNumber = d.WeekNumber,
                Title = d.Title,
                Description = d.Description,
                IsRestDay = d.IsRestDay,
                RequiresReflection = d.RequiresReflection,
                ActionRoute = d.ActionRoute,
                IsCompleted = p?.IsCompleted ?? false,
                CompletedAt = p?.CompletedAt,
                ReflectionText = p?.ReflectionText
            };
        }).ToList();

        var totalActionableDays = days.Count(d => !d.IsRestDay);
        var completedCount = dayDtos.Count(d => !d.IsRestDay && d.IsCompleted);

        var elapsedDays = (DateTime.UtcNow.Date - enrollment.StartedAt.Date).Days + 1;
        var currentDayNumber = Math.Clamp(elapsedDays, 1, 30);

        var overview = new ChallengeOverviewDto
        {
            StartedAt = enrollment.StartedAt,
            CurrentDayNumber = currentDayNumber,
            CurrentStreak = enrollment.CurrentStreak,
            LongestStreak = enrollment.LongestStreak,
            CompletedDaysCount = completedCount,
            TotalActionableDays = totalActionableDays,
            IsFullyCompleted = enrollment.CompletedAt.HasValue,
            CompletedAt = enrollment.CompletedAt,
            Days = dayDtos
        };

        return Result.Success(overview);
    }

    public async Task<Result<ChallengeDayDto>> MarkDayCompleteAsync(string userId, MarkDayCompleteDto dto)
    {
        var day = await _context.ChallengeDays.FindAsync(dto.ChallengeDayId);
        if (day == null)
            return Result.Failure<ChallengeDayDto>(new Error("Challenge.DayNotFound", "Challenge day not found."));

        var progress = await _context.UserChallengeProgresses
            .FirstOrDefaultAsync(p => p.UserId == userId && p.ChallengeDayId == dto.ChallengeDayId);

        var now = DateTime.UtcNow;
        bool wasAlreadyCompleted = progress?.IsCompleted ?? false;

        if (progress == null)
        {
            progress = new UserChallengeProgress
            {
                UserId = userId,
                ChallengeDayId = dto.ChallengeDayId,
                IsCompleted = true,
                CompletedAt = now,
                ReflectionText = dto.ReflectionText
            };
            _context.UserChallengeProgresses.Add(progress);
        }
        else
        {
            progress.IsCompleted = true;
            progress.CompletedAt = now;
            // Only overwrite reflection text if new text was actually sent —
            // lets the frontend call this endpoint again just to edit the
            // reflection without accidentally blanking it via a null.
            if (dto.ReflectionText != null)
                progress.ReflectionText = dto.ReflectionText;
        }

        // ── Update streak — only on a genuinely new completion, not on re-save ──
        if (!wasAlreadyCompleted)
        {
            var enrollment = await GetOrCreateEnrollmentAsync(userId);
            UpdateStreak(enrollment, now);
            await CheckAndAwardCompletionAsync(userId, enrollment);
        }

        await _context.SaveChangesAsync(default);

        return Result.Success(new ChallengeDayDto
        {
            Id = day.Id,
            DayNumber = day.DayNumber,
            WeekNumber = day.WeekNumber,
            Title = day.Title,
            Description = day.Description,
            IsRestDay = day.IsRestDay,
            RequiresReflection = day.RequiresReflection,
            ActionRoute = day.ActionRoute,
            IsCompleted = progress.IsCompleted,
            CompletedAt = progress.CompletedAt,
            ReflectionText = progress.ReflectionText
        });
    }

    public async Task<Result<bool>> UnmarkDayAsync(string userId, UnmarkDayDto dto)
    {
        var progress = await _context.UserChallengeProgresses
            .FirstOrDefaultAsync(p => p.UserId == userId && p.ChallengeDayId == dto.ChallengeDayId);

        if (progress == null || !progress.IsCompleted)
            return Result.Success(true); // already not-completed — nothing to do

        progress.IsCompleted = false;
        progress.CompletedAt = null;
        // ReflectionText is intentionally left in place — don't destroy writing
        // just because the user unchecked the box.

        // NOTE — v1 simplification: streak is NOT retroactively recalculated here.
        // Undoing a day doesn't decrement CurrentStreak/LongestStreak. Getting
        // streak math right on arbitrary-order undo (what if they undo a day
        // from 5 days ago?) is genuinely fiddly and low-value for a foundation
        // build. If this matters to you later, the correct fix is to derive
        // streak from UserChallengeProgress.CompletedAt dates on every read
        // instead of maintaining running counters on the enrollment — a bigger
        // change, worth doing deliberately rather than bolting on here.

        await _context.SaveChangesAsync(default);
        return Result.Success(true);
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    private async Task<UserChallengeEnrollment> GetOrCreateEnrollmentAsync(string userId)
    {
        var enrollment = await _context.UserChallengeEnrollments
            .FirstOrDefaultAsync(e => e.UserId == userId);

        if (enrollment == null)
        {
            enrollment = new UserChallengeEnrollment
            {
                UserId = userId,
                StartedAt = DateTime.UtcNow
            };
            _context.UserChallengeEnrollments.Add(enrollment);
            await _context.SaveChangesAsync(default);
        }

        return enrollment;
    }

    private static void UpdateStreak(UserChallengeEnrollment enrollment, DateTime now)
    {
        var today = now.Date;

        if (enrollment.LastActivityDate == today)
        {
            // Already logged activity today — streak doesn't change on a
            // second completion the same day.
            return;
        }

        if (enrollment.LastActivityDate == today.AddDays(-1))
        {
            enrollment.CurrentStreak += 1;
        }
        else
        {
            // First-ever activity, or there was a gap — streak restarts at 1.
            enrollment.CurrentStreak = 1;
        }

        enrollment.LongestStreak = Math.Max(enrollment.LongestStreak, enrollment.CurrentStreak);
        enrollment.LastActivityDate = today;
    }

    private async Task CheckAndAwardCompletionAsync(string userId, UserChallengeEnrollment enrollment)
    {
        if (enrollment.CompletedAt.HasValue)
            return; // already awarded, don't re-check every completion

        var totalActionable = await _context.ChallengeDays.CountAsync(d => !d.IsRestDay);
        var completedCount = await _context.UserChallengeProgresses
            .CountAsync(p => p.UserId == userId && p.IsCompleted);

        if (completedCount < totalActionable)
            return;

        enrollment.CompletedAt = DateTime.UtcNow;

        // Award the completion badge, reusing your existing Badge/UserBadge
        // tables from the Feedback Hub gamification system rather than
        // building a parallel reward mechanism.
        var badge = await _context.Badges.FirstOrDefaultAsync(b => b.Name == CompletionBadgeName);
        if (badge != null)
        {
            var alreadyAwarded = await _context.UserBadges
                .AnyAsync(ub => ub.UserId == userId && ub.BadgeId == badge.Id);

            if (!alreadyAwarded)
            {
                _context.UserBadges.Add(new UserBadge
                {
                    UserId = userId,
                    BadgeId = badge.Id,
                    AwardedAt = DateTime.UtcNow
                });
            }
        }
        // If the badge row doesn't exist yet (seeder hasn't run), we skip
        // awarding rather than fail the whole completion — CompletedAt is
        // still set correctly either way.
    }
}
