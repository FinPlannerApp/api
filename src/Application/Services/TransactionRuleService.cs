using Application.Common.Helpers;
using Application.Common.Models;
using Application.Contracts;
using Application.DTOs.Transactions;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Application.Services;

public interface ITransactionRuleService
{
    Task<Result<List<TransactionRuleDto>>> GetAllAsync(string userId);
    Task<Result<TransactionRuleDto>> UpsertAsync(string userId, UpsertTransactionRuleDto dto);
    Task<Result<bool>> DeleteAsync(string userId, int id);
    Task<Result<RuleMatchDto?>> MatchAsync(string userId, RuleMatchRequestDto dto);
}

public class TransactionRuleService : ITransactionRuleService
{
    private readonly IApplicationDbContext _context;
    public TransactionRuleService(IApplicationDbContext context) { _context = context; }

    public async Task<Result<List<TransactionRuleDto>>> GetAllAsync(string userId)
    {
        var rules = await _context.TransactionRules.Where(r => r.UserId == userId)
            .OrderBy(r => r.Priority).ThenBy(r => r.Id).ToListAsync();
        var catIds = rules.Where(r => r.TransactionCategoryId.HasValue).Select(r => r.TransactionCategoryId!.Value).Distinct().ToList();
        var merIds = rules.Where(r => r.MerchantId.HasValue).Select(r => r.MerchantId!.Value).Distinct().ToList();
        var cats = await _context.TransactionCategories.Where(c => catIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name);
        var mers = await _context.Merchants.Where(m => merIds.Contains(m.Id)).ToDictionaryAsync(m => m.Id, m => m.Name);
        return Result.Success(rules.Select(r => new TransactionRuleDto
        {
            Id = r.Id, Name = r.Name, MatchText = r.MatchText, MatchMode = r.MatchMode, TypeFilter = r.TypeFilter,
            MinAmount = r.MinAmount, MaxAmount = r.MaxAmount,
            TransactionCategoryId = r.TransactionCategoryId,
            CategoryName = r.TransactionCategoryId.HasValue && cats.TryGetValue(r.TransactionCategoryId.Value, out var cn) ? cn : null,
            MerchantId = r.MerchantId,
            MerchantName = r.MerchantId.HasValue && mers.TryGetValue(r.MerchantId.Value, out var mn) ? mn : null,
            Tags = TransactionTags.Unpack(r.Tags), Priority = r.Priority, IsActive = r.IsActive
        }).ToList());
    }

    public async Task<Result<TransactionRuleDto>> UpsertAsync(string userId, UpsertTransactionRuleDto dto)
    {
        var text = (dto.MatchText ?? string.Empty).Trim();
        if (text.Length == 0 || text.Length > 120)
            return Result.Failure<TransactionRuleDto>(new Error("Rule.InvalidText", "Enter the text to look for (up to 120 characters)."));
        if (dto.MatchMode is < 0 or > 2)
            return Result.Failure<TransactionRuleDto>(new Error("Rule.InvalidMode", "Match mode must be contains, starts with or equals."));
        if (dto.MinAmount < 0 || dto.MaxAmount < 0 || (dto.MinAmount.HasValue && dto.MaxAmount.HasValue && dto.MinAmount > dto.MaxAmount))
            return Result.Failure<TransactionRuleDto>(new Error("Rule.InvalidAmount", "Check the amount range."));
        if (dto.Priority is < 1 or > 1000)
            return Result.Failure<TransactionRuleDto>(new Error("Rule.InvalidPriority", "Priority must be between 1 and 1000."));
        var tags = TransactionTags.Pack(dto.Tags);
        if (!dto.TransactionCategoryId.HasValue && !dto.MerchantId.HasValue && tags == null)
            return Result.Failure<TransactionRuleDto>(new Error("Rule.NoAction", "Choose a category, merchant or tag for the rule to apply."));

        if (dto.TransactionCategoryId.HasValue &&
            !await _context.TransactionCategories.AnyAsync(c => c.Id == dto.TransactionCategoryId.Value && c.UserId == userId))
            return Result.Failure<TransactionRuleDto>(new Error("Rule.InvalidCategory", "That category doesn't belong to you."));
        if (dto.MerchantId.HasValue &&
            !await _context.Merchants.AnyAsync(m => m.Id == dto.MerchantId.Value && m.UserId == userId))
            return Result.Failure<TransactionRuleDto>(new Error("Rule.InvalidMerchant", "That merchant doesn't belong to you."));

        TransactionRule rule;
        if (dto.Id.HasValue && dto.Id.Value > 0)
        {
            var existing = await _context.TransactionRules.FirstOrDefaultAsync(r => r.Id == dto.Id.Value && r.UserId == userId);
            if (existing == null) return Result.Failure<TransactionRuleDto>(new Error("Rule.NotFound", "Rule not found."));
            rule = existing;
        }
        else
        {
            if (await _context.TransactionRules.CountAsync(r => r.UserId == userId) >= TransactionRuleMatcher.MaxRulesPerUser)
                return Result.Failure<TransactionRuleDto>(new Error("Rule.Limit", $"You can have up to {TransactionRuleMatcher.MaxRulesPerUser} rules."));
            rule = new TransactionRule { UserId = userId };
            _context.TransactionRules.Add(rule);
        }

        var name = (dto.Name ?? string.Empty).Trim();
        rule.Name = (name.Length == 0 ? text : name)[..Math.Min(80, (name.Length == 0 ? text : name).Length)];
        rule.MatchText = text;
        rule.MatchMode = dto.MatchMode;
        rule.TypeFilter = dto.TypeFilter;
        rule.MinAmount = dto.MinAmount;
        rule.MaxAmount = dto.MaxAmount;
        rule.TransactionCategoryId = dto.TransactionCategoryId;
        rule.MerchantId = dto.MerchantId;
        rule.Tags = tags;
        rule.Priority = dto.Priority;
        rule.IsActive = dto.IsActive;
        await _context.SaveChangesAsync();

        var all = await GetAllAsync(userId);
        return Result.Success(all.Value!.First(r => r.Id == rule.Id));
    }

    public async Task<Result<bool>> DeleteAsync(string userId, int id)
    {
        var rule = await _context.TransactionRules.FirstOrDefaultAsync(r => r.Id == id && r.UserId == userId);
        if (rule == null) return Result.Failure<bool>(new Error("Rule.NotFound", "Rule not found."));
        rule.IsDeleted = true;
        rule.DeletedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return Result.Success(true);
    }

    public async Task<Result<RuleMatchDto?>> MatchAsync(string userId, RuleMatchRequestDto dto)
    {
        var rule = await FindMatchAsync(_context, userId, dto.Description, dto.Amount, dto.Type);
        return Result.Success<RuleMatchDto?>(rule == null ? null : new RuleMatchDto
        {
            RuleId = rule.Id, RuleName = rule.Name, TransactionCategoryId = rule.TransactionCategoryId,
            MerchantId = rule.MerchantId, Tags = TransactionTags.Unpack(rule.Tags)
        });
    }

    /// <summary>Shared by the preview endpoint and by transaction saving.</summary>
    public static async Task<TransactionRule?> FindMatchAsync(IApplicationDbContext context, string userId, string? description, decimal amount, Domain.Enums.TransactionType type)
    {
        if (string.IsNullOrWhiteSpace(description)) return null;
        var rules = await context.TransactionRules.Where(r => r.UserId == userId && r.IsActive).ToListAsync();
        return TransactionRuleMatcher.FirstMatch(rules, description, amount, type);
    }
}
