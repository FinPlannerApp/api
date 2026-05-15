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
        var currentMonthStart = new DateTime(request.Year, request.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var now = currentMonthStart.AddMonths(1).AddTicks(-1); // End of selected month
        var lastMonthStart = currentMonthStart.AddMonths(-1);
        var lastMonthEnd = currentMonthStart.AddTicks(-1);

        // 1. Get User Accounts
        var accountIds = await _context.Accounts
            .Where(a => a.UserId == request.UserId)
            .Select(a => a.Id)
            .ToListAsync(cancellationToken);

        if (!accountIds.Any())
        {
            return Result.Success(new FinancialHealthDto { Score = 0, Status = "No Data" });
        }

        // 2. Fetch Current Month Transactions
        var currentTransactions = await _context.Transactions
            .AsNoTracking()
            .Where(t => accountIds.Contains(t.AccountId) && t.Date >= currentMonthStart)
            .ToListAsync(cancellationToken);

        var currentIncome = currentTransactions.Where(t => t.Type == TransactionType.Income).Sum(t => t.Amount);
        var currentExpense = currentTransactions.Where(t => t.Type == TransactionType.Expense).Sum(t => t.Amount);

        // 3. Savings Rate Calculation
        decimal savingsRate = 0;
        if (currentIncome > 0)
        {
            savingsRate = (currentIncome - currentExpense) / currentIncome;
        }

        // Score 1: Savings Rate (max 40 points)
        // >= 20% = 40 pts, <= 0% = 0 pts
        int savingsScore = (int)Math.Max(0, Math.Min(40, (savingsRate / 0.2m) * 40));

        // 4. Budget Adherence (max 40 points)
        var activeBudgets = await _context.Budgets
            .AsNoTracking()
            .Where(b => b.UserId == request.UserId && b.StartDate <= now && (b.EndDate == null || b.EndDate >= now))
            .ToListAsync(cancellationToken);

        int budgetScore = 40;
        decimal adherenceRate = 1.0m;
        if (activeBudgets.Any())
        {
            int exceededCount = 0;
            foreach (var budget in activeBudgets)
            {
                var spent = currentTransactions
                    .Where(t => t.Type == TransactionType.Expense && 
                               (!budget.TransactionCategoryId.HasValue || t.TransactionCategoryId == budget.TransactionCategoryId))
                    .Sum(t => t.Amount);

                if (spent > budget.Amount) exceededCount++;
            }
            adherenceRate = 1.0m - ((decimal)exceededCount / activeBudgets.Count);
            budgetScore = (int)(adherenceRate * 40);
        }

        // 5. Transaction Consistency (max 20 points)
        // Reward for tracking 5+ transactions this month
        int consistencyScore = Math.Min(20, currentTransactions.Count * 4);

        int totalScore = savingsScore + budgetScore + consistencyScore;

        var dto = new FinancialHealthDto
        {
            Score = totalScore,
            SavingsRate = savingsRate,
            BudgetAdherence = adherenceRate,
            Status = GetStatus(totalScore)
        };

        // 6. Generate Insights
        // Insight: Savings Rate
        if (savingsRate > 0.15m)
        {
            dto.Insights.Add(new FinancialHealthInsightDto { Message = "Excellent savings rate! You are saving over 15% of your income.", Type = "success" });
        }
        else if (savingsRate < 0)
        {
            dto.Insights.Add(new FinancialHealthInsightDto { Message = "Your spending exceeds your income this month. Consider reviewing your variable expenses.", Type = "warning" });
        }

        // Insight: Budget
        if (budgetScore < 30 && activeBudgets.Any())
        {
            dto.Insights.Add(new FinancialHealthInsightDto { Message = "You've exceeded several budgets. Check your 'Budgets' tab for details.", Type = "warning" });
        }

        // Insight: Category Trends (Last month vs current)
        var lastMonthExpensesByCategory = await _context.Transactions
            .AsNoTracking()
            .Where(t => accountIds.Contains(t.AccountId) && t.Date >= lastMonthStart && t.Date <= lastMonthEnd && t.Type == TransactionType.Expense)
            .GroupBy(t => t.TransactionCategoryId)
            .Select(g => new { CategoryId = g.Key, Total = g.Sum(t => t.Amount) })
            .ToListAsync(cancellationToken);

        foreach (var lastMonth in lastMonthExpensesByCategory.Where(x => x.CategoryId.HasValue).Take(2))
        {
            var currentMonthTotal = currentTransactions
                .Where(t => t.Type == TransactionType.Expense && t.TransactionCategoryId == lastMonth.CategoryId)
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
