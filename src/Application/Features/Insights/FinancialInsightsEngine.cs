using Application.Common.Helpers;
using Application.Contracts;
using Domain.Rules;
using Microsoft.EntityFrameworkCore;

namespace Application.Features.Insights;

public interface IFinancialInsightsEngine
{
    Task<List<FinancialInsight>> GenerateInsightsAsync(string userId, DateTime endDate);
}

public class FinancialInsightsEngine : IFinancialInsightsEngine
{
    private readonly IEnumerable<IFinancialRule> _rules;
    private readonly IApplicationDbContext _context;

    public FinancialInsightsEngine(IEnumerable<IFinancialRule> rules, IApplicationDbContext context)
    {
        _rules = rules;
        _context = context;
    }

    public async Task<List<FinancialInsight>> GenerateInsightsAsync(string userId, DateTime endDate)
    {
        var (startOfMonth, _) = AppTimeZone.MonthBoundsUtc(endDate);
        var (previousMonthStart, previousMonthEnd) = AppTimeZone.MonthBoundsUtc(endDate.AddMonths(-1));

        var currentExpenses = await _context.Transactions
            .Where(t => t.UserId == userId && t.Date >= startOfMonth && t.Date <= endDate &&
                        t.Type == Domain.Enums.TransactionType.Expense && t.TransferGroupId == null)
            .SumAsync(t => t.Amount);

        var currentIncome = await _context.Transactions
            .Where(t => t.UserId == userId && t.Date >= startOfMonth && t.Date <= endDate &&
                        t.Type == Domain.Enums.TransactionType.Income && t.TransferGroupId == null)
            .SumAsync(t => t.Amount);

        var previousExpenses = await _context.Transactions
            .Where(t => t.UserId == userId && t.Date >= previousMonthStart && t.Date <= previousMonthEnd &&
                        t.Type == Domain.Enums.TransactionType.Expense && t.TransferGroupId == null)
            .SumAsync(t => t.Amount);

        var previousIncome = await _context.Transactions
            .Where(t => t.UserId == userId && t.Date >= previousMonthStart && t.Date <= previousMonthEnd &&
                        t.Type == Domain.Enums.TransactionType.Income && t.TransferGroupId == null)
            .SumAsync(t => t.Amount);

        var activeSubscriptions = await _context.Subscriptions
            .Include(s => s.RecurringTransaction)
            .Where(s => s.UserId == userId && s.RecurringTransaction.IsActive)
            .SumAsync(s => s.RecurringTransaction.Amount);

        var budgetLimit = await _context.Budgets
            .Where(b => b.UserId == userId)
            .SumAsync(b => b.Amount);

        // ── Baseline for LifestyleInflationRule ───────────────────────────────
        var (sixMonthsAgoStart, _) = AppTimeZone.MonthBoundsUtc(endDate.AddMonths(-6));
        var (_, fourMonthsAgoEnd) = AppTimeZone.MonthBoundsUtc(endDate.AddMonths(-4));

        var baselineExpenseSum = await _context.Transactions
            .Where(t => t.UserId == userId && t.Type == Domain.Enums.TransactionType.Expense &&
                        t.TransferGroupId == null &&
                        t.Date >= sixMonthsAgoStart && t.Date <= fourMonthsAgoEnd)
            .SumAsync(t => t.Amount);

        var baselineMonthlyAverage = baselineExpenseSum / 3m;

        // ── Salary-day spike data for SalaryDaySpikeRule ──────────────────────
        // TransferGroupId == null matters especially here — without it, the
        // "largest income transaction" lookup could pick up a transfer's
        // income leg (someone moving money between their own accounts)
        // instead of an actual salary deposit, misidentifying "salary day"
        // entirely.
        var largestIncomeTransaction = await _context.Transactions
            .Where(t => t.UserId == userId && t.Date >= startOfMonth && t.Date <= endDate &&
                        t.Type == Domain.Enums.TransactionType.Income && t.TransferGroupId == null)
            .OrderByDescending(t => t.Amount)
            .FirstOrDefaultAsync();

        decimal spendingInThreeDaysAfterLargestIncome = 0;
        decimal averageDailySpendRestOfMonth = 0;

        if (largestIncomeTransaction != null)
        {
            var salaryDayLocal = largestIncomeTransaction.Date.ToLocal().Date;
            var threeDaysAfterStart = DateTime.SpecifyKind(salaryDayLocal, DateTimeKind.Unspecified).ToUtc();
            var threeDaysAfterEnd = DateTime.SpecifyKind(salaryDayLocal.AddDays(3), DateTimeKind.Unspecified).ToUtc();

            spendingInThreeDaysAfterLargestIncome = await _context.Transactions
                .Where(t => t.UserId == userId && t.Type == Domain.Enums.TransactionType.Expense &&
                            t.TransferGroupId == null &&
                            t.Date >= threeDaysAfterStart && t.Date < threeDaysAfterEnd)
                .SumAsync(t => t.Amount);

            var daysInPeriod = (endDate.ToLocal().Date - startOfMonth.ToLocal().Date).Days + 1;
            var remainingDays = Math.Max(1, daysInPeriod - 3);
            var restOfMonthExpense = currentExpenses - spendingInThreeDaysAfterLargestIncome;
            averageDailySpendRestOfMonth = restOfMonthExpense / remainingDays;
        }

        // ── Oldest active subscription age for SubscriptionReviewNudgeRule ────
        var oldestActiveSubscription = await _context.Subscriptions
            .Include(s => s.RecurringTransaction)
            .Where(s => s.UserId == userId && s.RecurringTransaction.IsActive)
            .OrderBy(s => s.RecurringTransaction.StartDate)
            .FirstOrDefaultAsync();

        int oldestSubscriptionMonths = 0;
        if (oldestActiveSubscription != null)
        {
            var start = oldestActiveSubscription.RecurringTransaction.StartDate;
            oldestSubscriptionMonths = Math.Max(0, ((endDate.Year - start.Year) * 12) + endDate.Month - start.Month);
        }

        var ruleContext = new RuleContext
        {
            UserId = userId,
            TotalIncome = currentIncome,
            TotalExpense = currentExpenses,
            PreviousIncome = previousIncome,
            PreviousExpense = previousExpenses,
            SubscriptionSpend = activeSubscriptions,
            BudgetLimit = budgetLimit,
            BaselineExpenseFourToSixMonthsAgo = baselineMonthlyAverage,
            SpendingInThreeDaysAfterLargestIncome = spendingInThreeDaysAfterLargestIncome,
            AverageDailySpendRestOfMonth = averageDailySpendRestOfMonth,
            OldestActiveSubscriptionMonths = oldestSubscriptionMonths
        };

        var results = new List<FinancialInsight>();

        if (ruleContext.PreviousExpense > 0)
        {
            var delta = ((ruleContext.TotalExpense - ruleContext.PreviousExpense) / ruleContext.PreviousExpense) * 100;
            if (delta > 20)
            {
                results.Add(new FinancialInsight
                {
                    Title = "Danger zone ⚠️ overspending",
                    Message = $"Your spending increased by {delta:F0}% compared to last month.",
                    Type = InsightType.Danger,
                    Triggered = true
                });
            }
            else if (delta < -10)
            {
                results.Add(new FinancialInsight
                {
                    Title = "You stayed under budget 🎯",
                    Message = $"Your spending dropped by {Math.Abs(delta):F0}% compared to last month! Great discipline.",
                    Type = InsightType.Positive,
                    Triggered = true
                });
            }
        }

        foreach (var rule in _rules)
        {
            var insight = rule.Evaluate(ruleContext);
            if (insight.Triggered)
            {
                results.Add(insight);
            }
        }

        return results;
    }
}
