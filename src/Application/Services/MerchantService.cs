using Application.Common.Models;
using Application.Contracts;
using Application.DTOs.Merchants;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Application.Services;

public interface IMerchantService
{
    Task<Result<List<MerchantDto>>> GetAllAsync(string userId);
    Task<Result<MerchantDto>> UpsertAsync(string userId, UpsertMerchantDto dto);
    Task<Result<bool>> DeleteAsync(string userId, int id);
    Task<Result<int?>> SuggestMerchantForDescriptionAsync(string userId, string description);
    Task<Result<List<MerchantSpendingDto>>> GetSpendingByMerchantAsync(string userId, DateTime startDate, DateTime endDate);
}

public class MerchantService : IMerchantService
{
    private readonly IApplicationDbContext _context;

    public MerchantService(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Result<List<MerchantDto>>> GetAllAsync(string userId)
    {
        var merchants = await _context.Merchants
            .Include(m => m.Aliases)
            .Where(m => m.UserId == userId)
            .OrderBy(m => m.Name)
            .Select(m => new MerchantDto
            {
                Id = m.Id,
                Name = m.Name,
                Aliases = m.Aliases.Select(a => a.Alias).ToList()
            })
            .ToListAsync();

        return Result.Success(merchants);
    }

    public async Task<Result<MerchantDto>> UpsertAsync(string userId, UpsertMerchantDto dto)
    {
        var exists = await _context.Merchants.AnyAsync(m =>
            m.UserId == userId && m.Name.ToLower() == dto.Name.Trim().ToLower() &&
            (!dto.Id.HasValue || m.Id != dto.Id.Value));

        if (exists)
            return Result.Failure<MerchantDto>(new Error("Merchant.Duplicate", $"A merchant named '{dto.Name}' already exists."));

        Merchant merchant;
        if (dto.Id.HasValue && dto.Id.Value > 0)
        {
            var existing = await _context.Merchants
                .Include(m => m.Aliases)
                .FirstOrDefaultAsync(m => m.Id == dto.Id.Value);

            if (existing == null || existing.UserId != userId)
                return Result.Failure<MerchantDto>(new Error("Merchant.NotFound", "Merchant not found."));

            existing.Name = dto.Name;
            // Replace the alias set wholesale on edit — simpler and safer
            // than trying to diff old vs. new, and aliases have no
            // identity of their own worth preserving across an edit.
            _context.MerchantAliases.RemoveRange(existing.Aliases);
            existing.Aliases = dto.Aliases
                .Where(a => !string.IsNullOrWhiteSpace(a))
                .Select(a => new MerchantAlias { Alias = a.Trim(), MerchantId = existing.Id })
                .ToList();

            _context.Merchants.Update(existing);
            merchant = existing;
        }
        else
        {
            merchant = new Merchant
            {
                UserId = userId,
                Name = dto.Name,
                Aliases = dto.Aliases
                    .Where(a => !string.IsNullOrWhiteSpace(a))
                    .Select(a => new MerchantAlias { Alias = a.Trim() })
                    .ToList()
            };
            _context.Merchants.Add(merchant);
        }

        await _context.SaveChangesAsync();

        return Result.Success(new MerchantDto
        {
            Id = merchant.Id,
            Name = merchant.Name,
            Aliases = merchant.Aliases.Select(a => a.Alias).ToList()
        });
    }

    public async Task<Result<bool>> DeleteAsync(string userId, int id)
    {
        var merchant = await _context.Merchants.FindAsync(id);
        if (merchant == null || merchant.UserId != userId)
            return Result.Failure<bool>(new Error("Merchant.NotFound", "Merchant not found."));

        merchant.IsDeleted = true;
        merchant.DeletedAt = DateTime.UtcNow;
        _context.Merchants.Update(merchant);
        await _context.SaveChangesAsync();

        return Result.Success(true);
    }

    /// <summary>
    /// Given a transaction description, suggests a matching merchant by
    /// checking whether the description CONTAINS any of the user's
    /// merchant names or aliases, after stripping everything but letters
    /// and digits from both sides. "ZEPTO*ORDER#12345" correctly matches
    /// an alias of "zepto" this way. Returns null if nothing matches —
    /// the caller (transaction form) treats this purely as a suggestion,
    /// never auto-applies it.
    /// </summary>
    public async Task<Result<int?>> SuggestMerchantForDescriptionAsync(string userId, string description)
    {
        if (string.IsNullOrWhiteSpace(description))
            return Result.Success<int?>(null);

        var normalizedDescription = NormalizeForMatching(description);

        var merchants = await _context.Merchants
            .Include(m => m.Aliases)
            .Where(m => m.UserId == userId)
            .ToListAsync();

        foreach (var merchant in merchants)
        {
            var candidates = new List<string> { merchant.Name };
            candidates.AddRange(merchant.Aliases.Select(a => a.Alias));

            foreach (var candidate in candidates)
            {
                var normalizedCandidate = NormalizeForMatching(candidate);
                if (normalizedCandidate.Length > 0 && normalizedDescription.Contains(normalizedCandidate))
                {
                    return Result.Success<int?>(merchant.Id);
                }
            }
        }

        return Result.Success<int?>(null);
    }

    public async Task<Result<List<MerchantSpendingDto>>> GetSpendingByMerchantAsync(string userId, DateTime startDate, DateTime endDate)
    {
        var accountIds = await _context.Accounts
            .Where(a => a.UserId == userId)
            .Select(a => a.Id)
            .ToListAsync();

        var spending = await _context.Transactions
            .Where(t => accountIds.Contains(t.AccountId) && t.MerchantId != null &&
                        t.Type == Domain.Enums.TransactionType.Expense &&
                        t.TransferGroupId == null &&
                        t.Date >= startDate && t.Date <= endDate)
            .GroupBy(t => new { t.MerchantId, t.Merchant!.Name })
            .Select(g => new MerchantSpendingDto
            {
                MerchantId = g.Key.MerchantId!.Value,
                MerchantName = g.Key.Name,
                TotalSpent = g.Sum(t => t.Amount),
                TransactionCount = g.Count()
            })
            .OrderByDescending(m => m.TotalSpent)
            .ToListAsync();

        return Result.Success(spending);
    }

    private static string NormalizeForMatching(string input)
    {
        return new string(input.ToLower().Where(char.IsLetterOrDigit).ToArray());
    }
}
