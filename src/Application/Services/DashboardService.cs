using Application.Common.Helpers;
using Application.Common.Models;
using Application.Contracts;
using Application.DTOs.Dashboard;
using Domain.Enums;
using Microsoft.AspNetCore.Identity;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Application.Features.Insights;
using Application.Features.RecurringTransactions.Queries;
using MediatR;

namespace Application.Services;

public class DashboardService : IDashboardService
{
    private readonly IApplicationDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ICacheService _cache;
    private readonly IFinancialInsightsEngine _insightsEngine;
    private readonly IMediator _mediator;

    private const int ObligationWindowDays = 30;

    private static string SummaryKey(string userId, DateTime start, DateTime end) => DashboardCacheKeys.Summary(userId, start, end);
    private static string CategoryKey(string userId, DateTime start, DateTime end) => DashboardCacheKeys.Categories(userId, start, end);
    private static string InsightsKey(string userId, DateTime start, DateTime end) => DashboardCacheKeys.Insights(userId, start, end);
    private static string UserPrefix(string userId) => DashboardCacheKeys.UserPrefix(userId);

    public DashboardService(IApplicationDbContext context, UserManager<ApplicationUser> userManager, ICacheService cache, IFinancialInsightsEngine insightsEngine, IMediator mediator)
    {
        _mediator = mediator;
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

    // ── Shared helper: resolves the effective UTC date range for a query ──────
    // If the caller supplied explicit dates, use them as-is (already UTC, per
    // existing convention). Otherwise default to "this month" — computed in
    // LOCAL time first, then converted to UTC. This is the fix for the
    // reported bug: a transaction saved at local midnight on the 1st no
    // longer falls outside this range just because its UTC-stored value
    // technically lands on the previous UTC day.
    private static (DateTime Start, DateTime End) ResolveMonthRange(DateTime? startDate, DateTime? endDate)
    {
        if (startDate.HasValue && endDate.HasValue)
            return (startDate.Value.EnsureUtc(), endDate.Value.EnsureUtc());

        var (monthStartUtc, monthEndUtc) = AppTimeZone.MonthBoundsUtc(DateTime.UtcNow);
        return ((startDate ?? monthStartUtc).EnsureUtc(), (endDate ?? monthEndUtc).EnsureUtc());
    }

    public async Task<Result<DashboardSummaryDto>> GetSummaryAsync(string userId, DateTime? startDate = null, DateTime? endDate = null)
    {
        var (startFilter, endFilter) = ResolveMonthRange(startDate, endDate);

        var cacheKey = SummaryKey(userId, startFilter, endFilter);
        var cached = await _cache.GetAsync<DashboardSummaryDto>(cacheKey);
        if (cached is not null)
            return Result.Success(cached);

        var netWorthResult = await NetWorthCalculator.CalculateAsync(_context, userId);
        var netWorth = netWorthResult.NetWorth;

        var liquidAssets = await _context.Accounts
            .Where(a => a.UserId == userId &&
                (a.AccountCategory.AccountType == Domain.Enums.AccountType.Bank ||
                 a.AccountCategory.AccountType == Domain.Enums.AccountType.Cash))
            .SumAsync(a => a.Balance);

        var creditCardAccounts = await _context.Accounts
            .Include(a => a.AccountCategory)
            .Include(a => a.CreditCardDetails)
            .Where(a => a.UserId == userId && a.AccountCategory.AccountType == AccountType.CreditCard)
            .ToListAsync();

        var creditCardDebt = creditCardAccounts.Sum(a => a.Balance);
        var realAvailableCash = liquidAssets + creditCardDebt;

        decimal reservedForPayment = 0;
        foreach (var card in creditCardAccounts)
        {
            var recentBill = await _context.CreditCardBills
                .Where(b => b.AccountId == card.Id)
                .OrderByDescending(b => b.StatementDate)
                .FirstOrDefaultAsync();

            if (recentBill != null)
            {
                // Only what is STILL unpaid on the latest statement — a settled or
                // part-paid bill must stop counting as "reserved".
                reservedForPayment += await CreditCardBillMath.RemainingAsync(_context, recentBill);
            }
            else
            {
                var breakdown = await CreditCardStatementCalculator.CalculateAsync(_context, card);
                reservedForPayment += breakdown.StatementOutstanding;
            }
        }

        // Cash (bank + cash accounts) at the start of the selected month: today's liquid
        // balance minus every movement on those accounts since the 1st. Unlike the old
        // "sum of everything before this month across all accounts", this is a real balance
        // you can check against your statements.
        var liquidMovementSinceStart = await _context.Transactions
            .Where(t => t.Account.UserId == userId && t.Date >= startFilter &&
                (t.Account.AccountCategory.AccountType == AccountType.Bank ||
                 t.Account.AccountCategory.AccountType == AccountType.Cash))
            .SumAsync(t => t.Type == TransactionType.Income ? t.Amount : -t.Amount);
        var broughtForward = liquidAssets - liquidMovementSinceStart;

        var monthlyTransactions = _context.Transactions
            .Where(t => t.Account.UserId == userId && t.Date >= startFilter && t.Date <= endFilter);

        var monthlyIncome = await monthlyTransactions
            .Where(t => t.Type == TransactionType.Income && t.TransferGroupId == null)
            .SumAsync(t => t.Amount);

        var monthlyExpenses = await monthlyTransactions
            .Where(t => t.Type == TransactionType.Expense && t.TransferGroupId == null)
            .SumAsync(t => t.Amount);

        // Safe-to-Spend: what's left after commitments that Real Available Cash does not
        // yet reflect. Card statement bills are excluded (ReducesAvailableCash == false)
        // because the card's debt is already subtracted above.
        decimal upcomingObligations = 0;
        var obligationsResult = await _mediator.Send(new GetUpcomingObligationsQuery(userId, ObligationWindowDays));
        if (obligationsResult.IsSuccess && obligationsResult.Value != null)
        {
            upcomingObligations = obligationsResult.Value
                .Where(o => o.ReducesAvailableCash && o.Amount.HasValue)
                .Sum(o => o.Amount!.Value);
        }

        var summary = new DashboardSummaryDto
        {
            LiquidCash = liquidAssets,
            CreditCardDebt = -creditCardDebt,
            UpcomingObligationsTotal = upcomingObligations,
            SafeToSpend = realAvailableCash - upcomingObligations,
            ObligationWindowDays = ObligationWindowDays,
            NetWorth = netWorth,
            MonthlyIncome = monthlyIncome,
            MonthlyExpenses = monthlyExpenses,
            BroughtForwardAmount = broughtForward,
            RealAvailableCash = realAvailableCash,
            ReservedForPayment = reservedForPayment
        };

        await _cache.SetAsync(cacheKey, summary, TimeSpan.FromMinutes(5));
        return Result.Success(summary);
    }

    public async Task<Result<List<SpendingByCategoryDto>>> GetSpendingByCategoryAsync(string userId, DateTime? startDate = null, DateTime? endDate = null)
    {
        var (startFilter, endFilter) = ResolveMonthRange(startDate, endDate);

        var cacheKey = CategoryKey(userId, startFilter, endFilter);
        var cached = await _cache.GetAsync<List<SpendingByCategoryDto>>(cacheKey);
        if (cached is not null)
            return Result.Success(cached);

        var spendingData = await _context.Transactions
            .Where(t =>
                t.Account.UserId == userId &&
                t.Type == TransactionType.Expense &&
                t.TransferGroupId == null &&
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

        var (startFilter, endFilter) = ResolveMonthRange(startDate, endDate);

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
        var (startFilter, endFilter) = ResolveMonthRange(startDate, endDate);

        var cacheKey = InsightsKey(userId, startFilter, endFilter);
        var cached = await _cache.GetAsync<DashboardInsightsDto>(cacheKey);
        if (cached is not null)
            return Result.Success(cached);

        var query = _context.Transactions
            .Include(t => t.TransactionCategory)
            .Include(t => t.Account)
            .Where(t => t.Account.UserId == userId && t.Date >= startFilter && t.Date <= endFilter &&
                        t.TransferGroupId == null);

        var insights = new DashboardInsightsDto();

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

        insights.HighestAmountTransactions = baseSet.OrderByDescending(t => t.Amount).Take(5).ToList();
        insights.LowestAmountTransactions = baseSet.OrderBy(t => t.Amount).Take(5).ToList();

        insights.LatestTransactions = baseSet.OrderByDescending(t => t.Date).Take(5).ToList();
        insights.OldestTransactions = baseSet.OrderBy(t => t.Date).Take(5).ToList();

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

    public async Task<Result<List<MonthlyTrendPointDto>>> GetMonthlyTrendAsync(string userId, int monthsBack = 6)
    {
        var results = new List<MonthlyTrendPointDto>();
        var now = DateTime.UtcNow;

        // monthsBack-1 down to 0, so results come back oldest-first —
        // matches what a chart's x-axis expects without the frontend
        // needing to reverse anything.
        for (int i = monthsBack - 1; i >= 0; i--)
        {
            var (monthStart, monthEnd) = AppTimeZone.MonthBoundsUtc(now.AddMonths(-i));

            // Excludes transfers, same fix as everywhere else this
            // project computes income/expense — a trend chart built on
            // the unfixed numbers would just make the double-counting
            // bug permanent and visible on a chart instead of fixed.
            var income = await _context.Transactions
                .Where(t => t.Account.UserId == userId && t.Date >= monthStart && t.Date <= monthEnd &&
                            t.Type == TransactionType.Income && t.TransferGroupId == null)
                .SumAsync(t => t.Amount);

            var expense = await _context.Transactions
                .Where(t => t.Account.UserId == userId && t.Date >= monthStart && t.Date <= monthEnd &&
                            t.Type == TransactionType.Expense && t.TransferGroupId == null)
                .SumAsync(t => t.Amount);

            var localMonth = monthStart.ToLocal();
            results.Add(new MonthlyTrendPointDto
            {
                Year = localMonth.Year,
                Month = localMonth.Month,
                Income = income,
                Expense = expense
            });
        }

        return Result.Success(results);
    }
}
