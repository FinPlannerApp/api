using Application.Common.Models;
using Application.Contracts;
using Application.DTOs.SavingsBuckets;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Application.Services;

public interface ISavingsBucketService
{
    Task<Result<AccountBucketBreakdownDto>> GetBucketsForAccountAsync(string userId, int accountId);
    Task<Result<List<SavingsBucketDto>>> GetAllForUserAsync(string userId);
    Task<Result<SavingsBucketDto>> UpsertAsync(string userId, UpsertSavingsBucketDto dto);
    Task<Result<bool>> DeleteAsync(string userId, int id);
}

public class SavingsBucketService : ISavingsBucketService
{
    private readonly IApplicationDbContext _context;

    public SavingsBucketService(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Result<AccountBucketBreakdownDto>> GetBucketsForAccountAsync(string userId, int accountId)
    {
        var account = await _context.Accounts.FindAsync(accountId);
        if (account == null || account.UserId != userId)
            return Result.Failure<AccountBucketBreakdownDto>(new Error("Account.NotFound", "Account not found."));

        var buckets = await _context.SavingsBuckets
            .Where(b => b.AccountId == accountId && b.UserId == userId)
            .OrderByDescending(b => b.AllocatedAmount)
            .Select(b => new SavingsBucketDto
            {
                Id = b.Id,
                AccountId = b.AccountId,
                AccountName = account.Name,
                Name = b.Name,
                AllocatedAmount = b.AllocatedAmount,
                TargetAmount = b.TargetAmount
            })
            .ToListAsync();

        var totalAllocated = buckets.Sum(b => b.AllocatedAmount);

        return Result.Success(new AccountBucketBreakdownDto
        {
            AccountId = accountId,
            AccountBalance = account.Balance,
            TotalAllocated = totalAllocated,
            Unallocated = account.Balance - totalAllocated,
            Buckets = buckets
        });
    }

    public async Task<Result<SavingsBucketDto>> UpsertAsync(string userId, UpsertSavingsBucketDto dto)
    {
        var account = await _context.Accounts.FindAsync(dto.AccountId);
        if (account == null || account.UserId != userId)
            return Result.Failure<SavingsBucketDto>(new Error("Account.NotFound", "Account not found."));

        if (dto.AllocatedAmount < 0)
            return Result.Failure<SavingsBucketDto>(new Error("Bucket.InvalidAmount", "Allocated amount can't be negative."));

        // The actual validation this whole feature depends on being
        // trustworthy — sum of every OTHER bucket for this account, plus
        // whatever this one is being set to, must not exceed the
        // account's real balance. Excludes the bucket being edited from
        // "other buckets" so editing an existing bucket doesn't double-count it.
        var otherBucketsTotal = await _context.SavingsBuckets
            .Where(b => b.AccountId == dto.AccountId && b.UserId == userId && (!dto.Id.HasValue || b.Id != dto.Id.Value))
            .SumAsync(b => b.AllocatedAmount);

        if (otherBucketsTotal + dto.AllocatedAmount > account.Balance)
        {
            var available = account.Balance - otherBucketsTotal;
            return Result.Failure<SavingsBucketDto>(new Error(
                "Bucket.ExceedsBalance",
                $"This would allocate more than the account actually holds. Available to allocate: ₹{available:F2}."));
        }

        SavingsBucket bucket;
        if (dto.Id.HasValue && dto.Id.Value > 0)
        {
            var existing = await _context.SavingsBuckets.FindAsync(dto.Id.Value);
            if (existing == null || existing.UserId != userId)
                return Result.Failure<SavingsBucketDto>(new Error("Bucket.NotFound", "Bucket not found."));

            existing.Name = dto.Name;
            existing.AllocatedAmount = dto.AllocatedAmount;
            existing.TargetAmount = dto.TargetAmount;
            existing.AccountId = dto.AccountId;
            _context.SavingsBuckets.Update(existing);
            bucket = existing;
        }
        else
        {
            bucket = new SavingsBucket
            {
                UserId = userId,
                AccountId = dto.AccountId,
                Name = dto.Name,
                AllocatedAmount = dto.AllocatedAmount,
                TargetAmount = dto.TargetAmount
            };
            _context.SavingsBuckets.Add(bucket);
        }

        await _context.SaveChangesAsync();

        return Result.Success(new SavingsBucketDto
        {
            Id = bucket.Id,
            AccountId = bucket.AccountId,
            AccountName = account.Name,
            Name = bucket.Name,
            AllocatedAmount = bucket.AllocatedAmount,
            TargetAmount = bucket.TargetAmount
        });
    }

    public async Task<Result<bool>> DeleteAsync(string userId, int id)
    {
        var bucket = await _context.SavingsBuckets.FindAsync(id);
        if (bucket == null || bucket.UserId != userId)
            return Result.Failure<bool>(new Error("Bucket.NotFound", "Bucket not found."));

        bucket.IsDeleted = true;
        bucket.DeletedAt = DateTime.UtcNow;
        _context.SavingsBuckets.Update(bucket);
        await _context.SaveChangesAsync();

        return Result.Success(true);
    }

    public async Task<Result<List<SavingsBucketDto>>> GetAllForUserAsync(string userId)
    {
        var buckets = await _context.SavingsBuckets
            .Include(b => b.Account)
            .Where(b => b.UserId == userId)
            .OrderBy(b => b.Account.Name).ThenByDescending(b => b.AllocatedAmount)
            .Select(b => new SavingsBucketDto
            {
                Id = b.Id,
                AccountId = b.AccountId,
                AccountName = b.Account.Name,
                Name = b.Name,
                AllocatedAmount = b.AllocatedAmount,
                TargetAmount = b.TargetAmount
            })
            .ToListAsync();

        return Result.Success(buckets);
    }
}
