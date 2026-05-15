using Application.Common.Models;
using Application.Contracts;
using Application.DTOs.Dashboard;
using Domain.Enums;
using Microsoft.AspNetCore.Identity;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Application.Features.Insights;

namespace Application.Services;

public class DashboardService : IDashboardService
{
    private readonly IApplicationDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ICacheService _cache;
    private readonly IFinancialInsightsEngine _insightsEngine;

    private static string SummaryKey(string userId, DateTime start, DateTime end)
        => $"dash:sum:{userId}:{start:yyyyMMdd}:{end:yyyyMMdd}";
    private static string CategoryKey(string userId, DateTime start, DateTime end)
        => $"dash:cat:{userId}:{start:yyyyMMdd}:{end:yyyyMMdd}";
    private static string InsightsKey(string userId, DateTime start, DateTime end)
        => $"dash:ins:{userId}:{start:yyyyMMdd}:{end:yyyyMMdd}";
    private static string UserPrefix(string userId) => $"dash::{userId}:";

    public DashboardService(IApplicationDbContext context, UserManager<ApplicationUser> userManager, ICacheService cache, IFinancialInsightsEngine insightsEngine)
    {
        _context = context;
        _userManager = userManager;
        _cache = cache;
        _insightsEngine = insightsEngine;
    }

    public async Task<Result<PublicStatsDto>> GetPublicStatsAsync()
    {
        var totalUsers = await _userManager.Users.CountAsync();
        var totalAccounts = await _context.Accounts.CountAsync();
        var totalTransactions = await _context.Transactions.CountAsync();
        var totalAmount = await _context.Transactions.SumAsync(t => Math.Abs(t.Amount));

        return Result.Success(new PublicStatsDto
        {
            TotalUsers = totalUsers,
            TotalAccounts = totalAccounts,
            TotalTransactions = totalTransactions,
            TotalTransactionVolume = totalAmount
        });
    }

    public async Task<Result<DashboardSummaryDto>> GetSummaryAsync(string userId, DateTime? startDate = null, DateTime? endDate = null)
    {
        var now = DateTime.UtcNow;
        var startFilter = startDate ?? new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var endFilter = endDate ?? startFilter.AddMonths(1).AddDays(-1);

        var cacheKey = SummaryKey(userId, startFilter, endFilter);
        var cached = await _cache.GetAsync<DashboardSummaryDto>(cacheKey);
        if (cached is not null)
            return Result.Success(cached);

        var netWorth = await _context.Accounts
            .Where(a => a.UserId == userId && 
                       a.AccountCategory.Name != "Credit Card" && 
                       a.AccountCategory.Name != "Loan")
            .SumAsync(a => a.Balance);

        var broughtForward = await _context.Transactions
            .Where(t => t.Account.UserId == userId && t.Date < startFilter)
            .SumAsync(t => t.Type == TransactionType.Income ? t.Amount : -t.Amount);

        var monthlyTransactions = _context.Transactions
            .Where(t => t.Account.UserId == userId && t.Date >= startFilter && t.Date <= endFilter);

        var monthlyIncome = await monthlyTransactions
            .Where(t => t.Type == TransactionType.Income)
            .SumAsync(t => t.Amount);

        var monthlyExpenses = await monthlyTransactions
            .Where(t => t.Type == TransactionType.Expense)
            .SumAsync(t => t.Amount);

        var summary = new DashboardSummaryDto
        {
            NetWorth = netWorth,
            MonthlyIncome = monthlyIncome,
            MonthlyExpenses = monthlyExpenses,
            BroughtForwardAmount = broughtForward
        };

        await _cache.SetAsync(cacheKey, summary, TimeSpan.FromMinutes(5));
        return Result.Success(summary);
    }

    public async Task<Result<List<SpendingByCategoryDto>>> GetSpendingByCategoryAsync(string userId, DateTime? startDate = null, DateTime? endDate = null)
    {
        var now = DateTime.UtcNow;
        var startFilter = startDate ?? new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var endFilter = endDate ?? startFilter.AddMonths(1).AddDays(-1);

        var cacheKey = CategoryKey(userId, startFilter, endFilter);
        var cached = await _cache.GetAsync<List<SpendingByCategoryDto>>(cacheKey);
        if (cached is not null)
            return Result.Success(cached);

        var spendingData = await _context.Transactions
            .Where(t =>
                t.Account.UserId == userId &&
                t.Type == TransactionType.Expense &&
                t.Date >= startFilter && t.Date <= endFilter)
            .GroupBy(t => t.TransactionCategory != null ? t.TransactionCategory.Name : "Uncategorized")
            .Select(group => new SpendingByCategoryDto
            {
                CategoryName = group.Key,
                TotalAmount = group.Sum(t => t.Amount)
            })
            .OrderByDescending(d => d.TotalAmount)
            .ToListAsync();

        await _cache.SetAsync(cacheKey, spendingData, TimeSpan.FromMinutes(5));
        return Result.Success(spendingData);
    }

    public async Task<Result<AccountSummaryDto>> GetAccountSummaryAsync(string userId, int accountId, DateTime? startDate = null, DateTime? endDate = null)
    {
        var account = await _context.Accounts.FirstOrDefaultAsync(a => a.Id == accountId && a.UserId == userId);

        if (account == null)
            return Result.Failure<AccountSummaryDto>(new Error("Account.NotFound", "Account not found."));

        var now = DateTime.UtcNow;
        var startFilter = startDate ?? new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var endFilter = endDate ?? startFilter.AddMonths(1).AddTicks(-1);

        var monthlyTransactions = _context.Transactions
            .Where(t => t.AccountId == accountId && t.Date >= startFilter && t.Date <= endFilter);

        var totalIncome = await monthlyTransactions
            .Where(t => t.Type == TransactionType.Income)
            .SumAsync(t => t.Amount);

        var totalExpenses = await monthlyTransactions
            .Where(t => t.Type == TransactionType.Expense)
            .SumAsync(t => t.Amount);

        var broughtForward = await _context.Transactions
            .Where(t => t.AccountId == accountId && t.Date < startFilter)
            .SumAsync(t => t.Type == TransactionType.Income ? t.Amount : -t.Amount);

        var summary = new AccountSummaryDto
        {
            CurrentBalance = account.Balance,
            TotalIncome = totalIncome,
            TotalExpenses = totalExpenses,
            BroughtForwardAmount = broughtForward
        };

        return Result.Success(summary);
    }

    public async Task<Result<DashboardInsightsDto>> GetDeepInsightsAsync(string userId, DateTime? startDate = null, DateTime? endDate = null)
    {
        var now = DateTime.UtcNow;
        var startFilter = startDate ?? new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var endFilter = endDate ?? startFilter.AddMonths(1).AddDays(-1);

        var cacheKey = InsightsKey(userId, startFilter, endFilter);
        var cached = await _cache.GetAsync<DashboardInsightsDto>(cacheKey);
        if (cached is not null)
            return Result.Success(cached);

        var query = _context.Transactions
            .Include(t => t.TransactionCategory)
            .Include(t => t.Account)
            .Where(t => t.Account.UserId == userId && t.Date >= startFilter && t.Date <= endFilter);

        var insights = new DashboardInsightsDto();

        // High volume querying logic mapping to BasicTransactionDto
        var baseSet = await query.Select(t => new BasicTransactionDto
        {
            Id = t.Id,
            Description = t.Description,
            Amount = t.Amount,
            Date = t.Date,
            Type = (int)t.Type,
            CategoryName = t.TransactionCategory != null ? t.TransactionCategory.Name : "Uncategorized",
            AccountName = t.Account.Name
        }).ToListAsync();

        if (!baseSet.Any()) 
            return Result.Success(insights);

        // Highest and Lowest
        insights.HighestAmountTransactions = baseSet.OrderByDescending(t => t.Amount).Take(5).ToList();
        insights.LowestAmountTransactions = baseSet.OrderBy(t => t.Amount).Take(5).ToList();

        // Latest and Oldest
        insights.LatestTransactions = baseSet.OrderByDescending(t => t.Date).Take(5).ToList();
        insights.OldestTransactions = baseSet.OrderBy(t => t.Date).Take(5).ToList();

        // Category Extremes
        var categoryGroups = baseSet.GroupBy(t => t.CategoryName);
        foreach (var group in categoryGroups.Where(g => g.Key != null))
        {
            insights.CategoryExtremes.Add(new GroupExtremeDto
            {
                GroupName = group.Key!,
                MaxTransaction = group.OrderByDescending(t => t.Amount).FirstOrDefault(),
                MinTransaction = group.OrderBy(t => t.Amount).FirstOrDefault()
            });
        }

        // Account Extremes
        var accountGroups = baseSet.GroupBy(t => t.AccountName);
        foreach (var group in accountGroups.Where(g => g.Key != null))
        {
            insights.AccountExtremes.Add(new GroupExtremeDto
            {
                GroupName = group.Key!,
                MaxTransaction = group.OrderByDescending(t => t.Amount).FirstOrDefault(),
                MinTransaction = group.OrderBy(t => t.Amount).FirstOrDefault()
            });
        }

        // --- NEW: Generate Financial Intelligence ---
        var intelligence = await _insightsEngine.GenerateInsightsAsync(userId, endFilter);
        insights.Intelligence = intelligence.Select(r => new FinancialInsightDto
        {
            Title = r.Title,
            Message = r.Message,
            Type = r.Type.ToString()
        }).ToList();

        await _cache.SetAsync(cacheKey, insights, TimeSpan.FromMinutes(30));

        return Result.Success(insights);
    }
}
