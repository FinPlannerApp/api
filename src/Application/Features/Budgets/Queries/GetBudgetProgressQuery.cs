using Application.Common.Models;
using Application.Contracts;
using Application.DTOs.Budgets;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Application.Features.Budgets.Queries;

public record GetBudgetProgressQuery(string UserId, DateTime RequestDate)
    : IRequest<Result<List<BudgetProgressDto>>>;

/// <summary>Named type for grouped spending results — avoids anonymous-type
/// incompatibility issues when conditionally building the weekly query.</summary>
internal record CategorySpend(int? CategoryId, decimal Total);

/// <summary>
/// Returns budget utilisation progress for the requesting user.
///
/// KEY FIX (earlier): accountIds fetched ONCE before the loop instead of
/// per-budget, and spending fetched in one grouped query instead of N+1.
///
/// NEW: Weekly period support. Monthly/Yearly still use the single grouped
/// month-level query below (untouched, zero risk to what's already verified
/// working in production). Weekly budgets can't use that same grouping —
/// month-level granularity can't tell you what happened within one week —
/// so they get a small, separate, targeted query that only runs at all if
/// at least one Weekly-period budget is actually active. No added cost for
/// users who only use Monthly/Yearly budgets.
/// </summary>
public class GetBudgetProgressQueryHandler
    : IRequestHandler<GetBudgetProgressQuery, Result<List<BudgetProgressDto>>>
{
    private readonly IApplicationDbContext _context;

    public GetBudgetProgressQueryHandler(IApplicationDbContext context)
        => _context = context;

    public async Task<Result<List<BudgetProgressDto>>> Handle(
        GetBudgetProgressQuery request,
        CancellationToken cancellationToken)
    {
        var monthStart = new DateTime(
            request.RequestDate.Year, request.RequestDate.Month, 1,
            0, 0, 0, DateTimeKind.Utc);
        var monthEnd = monthStart.AddMonths(1).AddTicks(-1);

        var yearStart = new DateTime(request.RequestDate.Year, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var yearEnd   = yearStart.AddYears(1).AddTicks(-1);

        // ── Week window: Monday–Sunday containing RequestDate (ISO-8601) ──────
        // DayOfWeek.Sunday = 0 in .NET, so this offset math treats Monday as
        // the start of the week regardless of locale.
        int isoDayOfWeek = ((int)request.RequestDate.DayOfWeek + 6) % 7; // Mon=0 .. Sun=6
        var weekStart = request.RequestDate.Date.AddDays(-isoDayOfWeek);
        var weekEnd   = weekStart.AddDays(7).AddTicks(-1);

        // ── 1. Load active budgets ────────────────────────────────────────────
        var activeBudgets = await _context.Budgets
            .AsNoTracking()
            .Where(b => b.UserId == request.UserId
                     && !b.IsDeleted
                     && b.StartDate <= monthEnd
                     && (b.EndDate == null || b.EndDate >= monthStart))
            .Include(b => b.TransactionCategory)
            .ToListAsync(cancellationToken);

        if (!activeBudgets.Any())
            return Result<List<BudgetProgressDto>>.Success(new List<BudgetProgressDto>());

        // ── 2. Fetch account IDs ONCE ───────────────────────────────────────────
        var accountIds = await _context.Accounts
            .AsNoTracking()
            .Where(a => a.UserId == request.UserId && !a.IsDeleted)
            .Select(a => a.Id)
            .ToListAsync(cancellationToken);

        if (!accountIds.Any())
            return Result<List<BudgetProgressDto>>.Success(new List<BudgetProgressDto>());

        // ── 3. Single grouped query for Monthly/Yearly spending data ──────────
        var spendingByCategory = await _context.Transactions
            .AsNoTracking()
            .Where(t => accountIds.Contains(t.AccountId)
                     && !t.IsDeleted
                     && t.Type == Domain.Enums.TransactionType.Expense
                     && t.Date >= yearStart
                     && t.Date <= yearEnd)
            .GroupBy(t => new { t.TransactionCategoryId, t.Date.Month })
            .Select(g => new
            {
                CategoryId = g.Key.TransactionCategoryId,
                Month      = g.Key.Month,
                Total      = g.Sum(t => t.Amount)
            })
            .ToListAsync(cancellationToken);

        // ── 3b. Separate query for Weekly spending — only runs if actually needed ──
        bool hasWeeklyBudgets = activeBudgets.Any(b => b.Period == Domain.Enums.BudgetPeriod.Weekly);

        List<CategorySpend> spendingByCategoryWeekly;
        if (hasWeeklyBudgets)
        {
            spendingByCategoryWeekly = await _context.Transactions
                .AsNoTracking()
                .Where(t => accountIds.Contains(t.AccountId)
                         && !t.IsDeleted
                         && t.Type == Domain.Enums.TransactionType.Expense
                         && t.Date >= weekStart
                         && t.Date <= weekEnd)
                .GroupBy(t => t.TransactionCategoryId)
                .Select(g => new CategorySpend(g.Key, g.Sum(t => t.Amount)))
                .ToListAsync(cancellationToken);
        }
        else
        {
            spendingByCategoryWeekly = new List<CategorySpend>();
        }

        // ── 4. Build progress list from in-memory data ────────────────────────
        var progressList = new List<BudgetProgressDto>();

        foreach (var budget in activeBudgets)
        {
            decimal spentAmount;

            switch (budget.Period)
            {
                case Domain.Enums.BudgetPeriod.Weekly:
                    spentAmount = budget.TransactionCategoryId.HasValue
                        ? spendingByCategoryWeekly
                            .Where(s => s.CategoryId == budget.TransactionCategoryId.Value)
                            .Sum(s => s.Total)
                        : spendingByCategoryWeekly.Sum(s => s.Total);
                    break;

                case Domain.Enums.BudgetPeriod.Monthly:
                    var relevantRows = spendingByCategory
                        .Where(s => s.Month == request.RequestDate.Month);

                    spentAmount = budget.TransactionCategoryId.HasValue
                        ? relevantRows
                            .Where(s => s.CategoryId == budget.TransactionCategoryId.Value)
                            .Sum(s => s.Total)
                        : relevantRows.Sum(s => s.Total);
                    break;

                default: // Yearly
                    spentAmount = budget.TransactionCategoryId.HasValue
                        ? spendingByCategory
                            .Where(s => s.CategoryId == budget.TransactionCategoryId.Value)
                            .Sum(s => s.Total)
                        : spendingByCategory.Sum(s => s.Total);
                    break;
            }

            spentAmount = Math.Abs(spentAmount);

            progressList.Add(new BudgetProgressDto
            {
                BudgetId              = budget.Id,
                TransactionCategoryId = budget.TransactionCategoryId,
                CategoryName          = budget.TransactionCategory?.Name,
                BudgetAmount          = budget.Amount,
                SpentAmount           = spentAmount,
                Period                = budget.Period,
                StartDate             = budget.StartDate,
                EndDate               = budget.EndDate
            });
        }

        return Result<List<BudgetProgressDto>>.Success(
            progressList
                .OrderByDescending(p => p.PercentageUsed)
                .ToList());
    }
}
