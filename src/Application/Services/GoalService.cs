using Application.Common.Models;
using Application.Contracts;
using Application.DTOs.Goals;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Application.Services;

public interface IGoalService
{
    Task<Result<List<GoalDto>>> GetAllAsync(string userId);
    Task<Result<GoalDto>> UpsertAsync(string userId, UpsertGoalDto dto);
    Task<Result<bool>> DeleteAsync(string userId, int id);
}

public class GoalService : IGoalService
{
    private readonly IApplicationDbContext _context;

    public GoalService(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Result<List<GoalDto>>> GetAllAsync(string userId)
    {
        var goals = await _context.Goals
            .Include(g => g.SavingsBucket)
            .Where(g => g.UserId == userId)
            .OrderBy(g => g.TargetDate ?? DateTime.MaxValue)
            .ToListAsync();

        return Result.Success(goals.Select(MapToDto).ToList());
    }

    public async Task<Result<GoalDto>> UpsertAsync(string userId, UpsertGoalDto dto)
    {
        if (dto.TargetAmount <= 0)
            return Result.Failure<GoalDto>(new Error("Goal.InvalidAmount", "Target amount must be greater than zero."));

        if (dto.SavingsBucketId.HasValue)
        {
            var bucket = await _context.SavingsBuckets.FindAsync(dto.SavingsBucketId.Value);
            if (bucket == null || bucket.UserId != userId)
                return Result.Failure<GoalDto>(new Error("Goal.BucketNotFound", "Linked savings bucket not found."));
        }

        Goal goal;
        if (dto.Id.HasValue && dto.Id.Value > 0)
        {
            var existing = await _context.Goals
                .Include(g => g.SavingsBucket)
                .FirstOrDefaultAsync(g => g.Id == dto.Id.Value);

            if (existing == null || existing.UserId != userId)
                return Result.Failure<GoalDto>(new Error("Goal.NotFound", "Goal not found."));

            existing.Name = dto.Name;
            existing.TargetAmount = dto.TargetAmount;
            existing.TargetDate = dto.TargetDate;
            existing.SavingsBucketId = dto.SavingsBucketId;
            existing.ManualCurrentAmount = dto.ManualCurrentAmount;

            // Re-check achieved status against the new target/progress —
            // editing a goal (e.g. raising the target) can un-achieve it,
            // this shouldn't silently stay stuck at whatever it was before.
            var currentAmount = dto.SavingsBucketId.HasValue
                ? existing.SavingsBucket?.AllocatedAmount ?? 0
                : dto.ManualCurrentAmount;
            existing.IsAchieved = currentAmount >= dto.TargetAmount;

            _context.Goals.Update(existing);
            goal = existing;
        }
        else
        {
            decimal initialAmount = dto.SavingsBucketId.HasValue ? 0 : dto.ManualCurrentAmount; // bucket amount fetched fresh on read either way
            goal = new Goal
            {
                UserId = userId,
                Name = dto.Name,
                TargetAmount = dto.TargetAmount,
                TargetDate = dto.TargetDate,
                SavingsBucketId = dto.SavingsBucketId,
                ManualCurrentAmount = dto.ManualCurrentAmount,
                IsAchieved = initialAmount >= dto.TargetAmount
            };
            _context.Goals.Add(goal);
        }

        await _context.SaveChangesAsync();

        // Reload with the bucket included so MapToDto has what it needs —
        // needed specifically for the create path, where the navigation
        // property isn't populated yet on the in-memory `goal` object.
        var reloaded = await _context.Goals
            .Include(g => g.SavingsBucket)
            .FirstAsync(g => g.Id == goal.Id);

        return Result.Success(MapToDto(reloaded));
    }

    public async Task<Result<bool>> DeleteAsync(string userId, int id)
    {
        var goal = await _context.Goals.FindAsync(id);
        if (goal == null || goal.UserId != userId)
            return Result.Failure<bool>(new Error("Goal.NotFound", "Goal not found."));

        goal.IsDeleted = true;
        goal.DeletedAt = DateTime.UtcNow;
        _context.Goals.Update(goal);
        await _context.SaveChangesAsync();

        return Result.Success(true);
    }

    private static GoalDto MapToDto(Goal g)
    {
        var currentAmount = g.SavingsBucketId.HasValue
            ? g.SavingsBucket?.AllocatedAmount ?? 0
            : g.ManualCurrentAmount;

        var progressPercent = g.TargetAmount > 0
            ? Math.Min(100, (currentAmount / g.TargetAmount) * 100)
            : 0;

        decimal? requiredMonthlySaving = null;
        if (g.TargetDate.HasValue)
        {
            var remaining = g.TargetAmount - currentAmount;
            if (remaining <= 0)
            {
                requiredMonthlySaving = 0; // already there, nothing more needed
            }
            else
            {
                var monthsRemaining = (decimal)(g.TargetDate.Value - DateTime.UtcNow).TotalDays / 30.44m; // average days/month
                requiredMonthlySaving = monthsRemaining > 0
                    ? remaining / monthsRemaining
                    : remaining; // target date already passed — the whole remaining amount is "needed now"
            }
        }

        return new GoalDto
        {
            Id = g.Id,
            Name = g.Name,
            TargetAmount = g.TargetAmount,
            TargetDate = g.TargetDate,
            IsAchieved = g.IsAchieved,
            SavingsBucketId = g.SavingsBucketId,
            SavingsBucketName = g.SavingsBucket?.Name,
            CurrentAmount = currentAmount,
            ProgressPercent = progressPercent,
            RequiredMonthlySaving = requiredMonthlySaving
        };
    }
}
