using Application.Common.Helpers;
using Application.Common.Models;
using Application.Contracts;
using Application.DTOs.Dashboard;
using Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Application.Features.Dashboard.Queries;

public record GetFinancialHealthQuery(string UserId, int Month, int Year) : IRequest<Result<FinancialHealthDto>>;

public class GetFinancialHealthQueryHandler : IRequestHandler<GetFinancialHealthQuery, Result<FinancialHealthDto>>
{
    private readonly IApplicationDbContext _context;

    public GetFinancialHealthQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Result<FinancialHealthDto>> Handle(GetFinancialHealthQuery request, CancellationToken cancellationToken)
    {
        // FIX 1: was constructing month start with hardcoded DateTimeKind.Utc
        // directly from request.Year/Month — same timezone bug fixed
        // elsewhere this project, just never caught in this specific file
        // until now.
        var referenceDate = new DateTime(request.Year, request.Month, 1);
        var (currentMonthStart, currentMonthEnd) = AppTimeZone.MonthBoundsUtc(referenceDate);
        var (lastMonthStart, lastMonthEnd) = AppTimeZone.MonthBoundsUtc(referenceDate.AddMonths(-1));

        var accountIds = await _context.Accounts
            .Where(a => a.UserId == request.UserId)
            .Select(a => a.Id)
            .ToListAsync(cancellationToken);

        if (!accountIds.Any())
        {
            return Result.Success(new FinancialHealthDto { Score = 0, Status = "No Data" });
        }

        var rollupMap = await CategoryRollup.BuildRollupMapAsync(_context, request.UserId, cancellationToken);

        // FIX 2: was missing an upper bound entirely (only t.Date >=
        // currentMonthStart, no <=) — correct only by coincidence when
        // querying the current month; querying any PAST month would have
        // pulled in every transaction from then until today.
        // FIX 3: now excludes transfers (TransferGroupId == null) — the
        // same double-counting bug fixed in DashboardService/reports
        // earlier this session, just never applied here since this file
        // wasn't touched during that pass.
        var currentTransactions = await _context.Transactions
            .AsNoTracking()
            .Where(t => accountIds.Contains(t.AccountId) &&
                        t.Date >= currentMonthStart && t.Date <= currentMonthEnd &&
                        t.TransferGroupId == null)
            .ToListAsync(cancellationToken);

        var currentIncome = currentTransactions.Where(t => t.Type == TransactionType.Income).Sum(t => t.Amount);
        var currentExpense = currentTransactions.Where(t => t.Type == TransactionType.Expense).Sum(t => t.Amount);

        var components = new List<FinancialHealthComponentDto>();

        // ── 1. Savings Rate (25 pts) ────────────────────────────────────────
        decimal savingsRate = 0;
        if (currentIncome > 0)
        {
            savingsRate = (currentIncome - currentExpense) / currentIncome;
        }
        int savingsScore = (int)Math.Max(0, Math.Min(25, (savingsRate / 0.2m) * 25));
        components.Add(new FinancialHealthComponentDto
        {
            Name = "Savings Rate",
            PointsEarned = savingsScore,
            MaxPoints = 25,
            Explanation = currentIncome > 0
                ? $"Saving {savingsRate:P0} of income this month."
                : "No income recorded this month."
        });

        // ── 2. Budget Adherence (20 pts) ────────────────────────────────────
        var activeBudgets = await _context.Budgets
            .AsNoTracking()
            .Where(b => b.UserId == request.UserId && b.StartDate <= currentMonthEnd && (b.EndDate == null || b.EndDate >= currentMonthEnd))
            .ToListAsync(cancellationToken);

        int budgetScore = 20;
        decimal adherenceRate = 1.0m;
        if (activeBudgets.Any())
        {
            int exceededCount = 0;
            foreach (var budget in activeBudgets)
            {
                var spent = currentTransactions
                    .Where(t => t.Type == TransactionType.Expense &&
                                CategoryRollup.Matches(rollupMap, budget.TransactionCategoryId, t.TransactionCategoryId))
                    .Sum(t => t.Amount);

                if (spent > budget.Amount) exceededCount++;
            }
            adherenceRate = 1.0m - ((decimal)exceededCount / activeBudgets.Count);
            budgetScore = (int)(adherenceRate * 20);
        }
        components.Add(new FinancialHealthComponentDto
        {
            Name = "Budget Adherence",
            PointsEarned = budgetScore,
            MaxPoints = 20,
            Explanation = activeBudgets.Any()
                ? $"Within limit on {activeBudgets.Count - (int)(activeBudgets.Count * (1 - adherenceRate))} of {activeBudgets.Count} active budgets."
                : "No active budgets to measure against."
        });

        // ── 3. Transaction Consistency (10 pts) ─────────────────────────────
        int consistencyScore = Math.Min(10, currentTransactions.Count * 2);
        components.Add(new FinancialHealthComponentDto
        {
            Name = "Transaction Tracking",
            PointsEarned = consistencyScore,
            MaxPoints = 10,
            Explanation = $"{currentTransactions.Count} transactions logged this month."
        });

        // ── 4. Net Worth Trend (15 pts) — calculated from current Net Worth & 3-month net flow ──
        var (threeMonthsAgoStart, _) = AppTimeZone.MonthBoundsUtc(referenceDate.AddMonths(-3));

        var currentNetWorth = await _context.Accounts
            .Where(a => a.UserId == request.UserId && !a.AccountCategory.IsLiability)
            .SumAsync(a => a.Balance, cancellationToken);

        var netIncomeInPeriod = await _context.Transactions
            .Where(t => accountIds.Contains(t.AccountId) && t.Date >= threeMonthsAgoStart && t.Date <= currentMonthEnd && t.TransferGroupId == null)
            .SumAsync(t => t.Type == TransactionType.Income ? t.Amount : -t.Amount, cancellationToken);

        var threeMonthsAgoNetWorth = currentNetWorth - netIncomeInPeriod;

        int netWorthTrendScore;
        string netWorthExplanation;

        if (threeMonthsAgoNetWorth <= 0)
        {
            netWorthTrendScore = currentNetWorth > threeMonthsAgoNetWorth ? 15 : 8;
            netWorthExplanation = "Net worth trend measured from a near-zero or negative baseline.";
        }
        else
        {
            var growthRate = (currentNetWorth - threeMonthsAgoNetWorth) / threeMonthsAgoNetWorth;
            netWorthTrendScore = (int)Math.Max(0, Math.Min(15, ((growthRate + 0.05m) / 0.15m) * 15));
            netWorthExplanation = $"Net worth {(growthRate >= 0 ? "up" : "down")} {Math.Abs(growthRate):P0} over the last 3 months.";
        }

        components.Add(new FinancialHealthComponentDto
        {
            Name = "Net Worth Trend",
            PointsEarned = netWorthTrendScore,
            MaxPoints = 15,
            Explanation = netWorthExplanation
        });

        // ── 5. Emergency Fund Coverage (15 pts) — NEW, uses AccountType ─────
        var liquidBalance = await _context.Accounts
            .Where(a => a.UserId == request.UserId &&
                (a.AccountCategory.AccountType == AccountType.Bank || a.AccountCategory.AccountType == AccountType.Cash))
            .SumAsync(a => a.Balance, cancellationToken);

        // Average of the last 3 full months' expenses (excluding
        // transfers, same as everywhere else) — a single month can be
        // unusually low/high, 3 months smooths that out.
        var (threeMonthsBackStart, _) = AppTimeZone.MonthBoundsUtc(referenceDate.AddMonths(-3));
        var recentExpenseTotal = await _context.Transactions
            .Where(t => accountIds.Contains(t.AccountId) && t.Type == TransactionType.Expense &&
                        t.TransferGroupId == null && t.Date >= threeMonthsBackStart && t.Date <= currentMonthEnd)
            .SumAsync(t => t.Amount, cancellationToken);
        var averageMonthlyExpense = recentExpenseTotal / 3m;

        int emergencyFundScore;
        string emergencyFundExplanation;
        if (averageMonthlyExpense <= 0)
        {
            emergencyFundScore = 15; // no spending history to measure against — not penalized for it
            emergencyFundExplanation = "Not enough spending history yet to estimate coverage.";
        }
        else
        {
            var monthsCovered = liquidBalance / averageMonthlyExpense;
            // Standard personal-finance benchmark: 3-6 months of expenses
            // in liquid savings is the commonly cited target range.
            emergencyFundScore = (int)Math.Max(0, Math.Min(15, (monthsCovered / 6m) * 15));
            emergencyFundExplanation = $"Liquid savings cover about {monthsCovered:F1} months of average spending.";
        }
        components.Add(new FinancialHealthComponentDto
        {
            Name = "Emergency Fund Coverage",
            PointsEarned = emergencyFundScore,
            MaxPoints = 15,
            Explanation = emergencyFundExplanation
        });

        // ── 6. Credit Utilization (10 pts) — NEW, uses CreditCardDetails ────
        var creditCardAccounts = await _context.Accounts
            .Include(a => a.CreditCardDetails)
            .Where(a => a.UserId == request.UserId && a.AccountCategory.AccountType == AccountType.CreditCard)
            .ToListAsync(cancellationToken);

        var accountsWithLimits = creditCardAccounts.Where(a => a.CreditCardDetails?.CreditLimit > 0).ToList();

        int creditUtilizationScore;
        string creditUtilizationExplanation;
        if (!accountsWithLimits.Any())
        {
            creditUtilizationScore = 10; // no credit cards, or none with a limit set — not penalized either way
            creditUtilizationExplanation = "No credit cards with a limit set.";
        }
        else
        {
            var totalDebt = accountsWithLimits.Sum(a => Math.Abs(Math.Min(0, a.Balance)));
            var totalLimit = accountsWithLimits.Sum(a => a.CreditCardDetails!.CreditLimit!.Value);
            var utilization = totalLimit > 0 ? totalDebt / totalLimit : 0;
            // Standard credit-health benchmark: under 30% utilization is
            // considered good.
            creditUtilizationScore = (int)Math.Max(0, Math.Min(10, (1 - (utilization / 0.3m)) * 10));
            creditUtilizationExplanation = $"Using {utilization:P0} of available credit limit across {accountsWithLimits.Count} card(s).";
        }
        components.Add(new FinancialHealthComponentDto
        {
            Name = "Credit Utilization",
            PointsEarned = creditUtilizationScore,
            MaxPoints = 10,
            Explanation = creditUtilizationExplanation
        });

        // ── 7. Real Available Cash (5 pts) — NEW ────────────────────────────
        var creditCardDebtSum = await _context.Accounts
            .Where(a => a.UserId == request.UserId && a.AccountCategory.AccountType == AccountType.CreditCard)
            .SumAsync(a => a.Balance, cancellationToken);
        var realAvailableCash = liquidBalance + creditCardDebtSum; // debt is already negative

        int cashScore = realAvailableCash >= 0 ? 5 : 0;
        components.Add(new FinancialHealthComponentDto
        {
            Name = "Real Available Cash",
            PointsEarned = cashScore,
            MaxPoints = 5,
            Explanation = realAvailableCash >= 0
                ? "Liquid cash covers outstanding credit card debt."
                : "Outstanding credit card debt exceeds liquid cash on hand."
        });

        // ── Total, status, insights ──────────────────────────────────────────
        int totalScore = components.Sum(c => c.PointsEarned);

        var dto = new FinancialHealthDto
        {
            Score = totalScore,
            SavingsRate = savingsRate,
            BudgetAdherence = adherenceRate,
            Status = GetStatus(totalScore),
            Components = components
        };

        if (savingsRate > 0.15m)
        {
            dto.Insights.Add(new FinancialHealthInsightDto { Message = "Excellent savings rate! You are saving over 15% of your income.", Type = "success" });
        }
        else if (savingsRate < 0)
        {
            dto.Insights.Add(new FinancialHealthInsightDto { Message = "Your spending exceeds your income this month. Consider reviewing your variable expenses.", Type = "warning" });
        }

        if (budgetScore < 15 && activeBudgets.Any())
        {
            dto.Insights.Add(new FinancialHealthInsightDto { Message = "You've exceeded several budgets. Check your 'Budgets' tab for details.", Type = "warning" });
        }

        var lastMonthExpensesByCategory = await _context.Transactions
            .AsNoTracking()
            .Where(t => accountIds.Contains(t.AccountId) && t.Date >= lastMonthStart && t.Date <= lastMonthEnd &&
                        t.Type == TransactionType.Expense && t.TransferGroupId == null)
            .GroupBy(t => t.TransactionCategoryId)
            .Select(g => new { CategoryId = g.Key, Total = g.Sum(t => t.Amount) })
            .ToListAsync(cancellationToken);

        foreach (var lastMonth in lastMonthExpensesByCategory.Where(x => x.CategoryId.HasValue).Take(2))
        {
            var currentMonthTotal = currentTransactions
                .Where(t => t.Type == TransactionType.Expense &&
                            CategoryRollup.Matches(rollupMap, lastMonth.CategoryId, t.TransactionCategoryId))
                .Sum(t => t.Amount);

            if (currentMonthTotal > lastMonth.Total * 1.2m)
            {
                var categoryName = await _context.TransactionCategories
                    .Where(c => c.Id == lastMonth.CategoryId)
                    .Select(c => c.Name)
                    .FirstOrDefaultAsync(cancellationToken);

                dto.Insights.Add(new FinancialHealthInsightDto
                {
                    Message = $"Spending in '{categoryName}' is up by more than 20% compared to last month.",
                    Type = "info",
                    CategoryName = categoryName
                });
            }
        }

        return Result.Success(dto);
    }

    private string GetStatus(int score)
    {
        if (score >= 80) return "Excellent";
        if (score >= 60) return "Good";
        if (score >= 40) return "Fair";
        return "Needs Attention";
    }
}
