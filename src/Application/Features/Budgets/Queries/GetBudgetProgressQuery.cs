using Application.Common.Helpers;
using Application.Common.Models;
using Application.Contracts;
using Application.DTOs.Budgets;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Application.Features.Budgets.Queries;

public record GetBudgetProgressQuery(string UserId, DateTime RequestDate)
    : IRequest<Result<List<BudgetProgressDto>>>;

internal record CategorySpend(int? CategoryId, decimal Total);

/// <summary>
/// Returns budget utilisation progress for the requesting user.
///
/// KEY FIX (earlier pass): accountIds fetched ONCE before the loop instead
/// of per-budget, spending fetched in one grouped query instead of N+1.
///
/// KEY FIX (this pass, two parts):
///   1. Period boundaries (month/year/week) computed in LOCAL time
///      (AppTimeZone) before conversion to UTC for the DB range filter —
///      fixes which rows get INCLUDED near a period boundary.
///   2. The month-bucketing GROUPING KEY itself is no longer computed in
///      SQL via t.Date.Month (which extracts month from the raw UTC value,
///      the exact same bug at the grouping level rather than the boundary
///      level). Instead, raw rows are fetched within the UTC year range,
///      then grouped in-memory by t.Date.ToLocal().Month. EF Core/Npgsql
///      generally can't translate timezone conversion into SQL, so this is
///      the correct, portable way to get local-month grouping right — the
///      SQL WHERE clause stays a simple, index-friendly UTC range, only the
///      grouping happens client-side after materializing a year's worth of
///      a single user's transactions (a few hundred rows at most — no
///      performance concern at that scale).
///
/// Weekly period support: separate small windowed query, only runs if at
/// least one Weekly budget is active among the user's current budgets.
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
        var (monthStart, monthEnd) = AppTimeZone.MonthBoundsUtc(request.RequestDate);
        var (yearStart, yearEnd)   = AppTimeZone.YearBoundsUtc(request.RequestDate);
        var (weekStart, weekEnd)   = AppTimeZone.WeekBoundsUtc(request.RequestDate);

        var todayLocal = (request.RequestDate.Kind == DateTimeKind.Utc
            ? request.RequestDate.ToLocal()
            : request.RequestDate).Date;

        var requestLocalMonth = (request.RequestDate.Kind == DateTimeKind.Utc
            ? request.RequestDate.ToLocal()
            : request.RequestDate).Month;

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

        // ── 3. Fetch raw yearly expense rows (UTC range — correct boundary) ────
        // Grouping happens AFTER this, in memory, using local time. Don't group
        // by t.Date.Month here — that's the same bug at the SQL level.
        var rawYearExpenses = await _context.Transactions
            .AsNoTracking()
            .Where(t => accountIds.Contains(t.AccountId)
                     && !t.IsDeleted
                     && t.Type == Domain.Enums.TransactionType.Expense
                     && t.Date >= yearStart
                     && t.Date <= yearEnd)
            .Select(t => new { t.TransactionCategoryId, t.Date, t.Amount })
            .ToListAsync(cancellationToken);

        var spendingByCategory = rawYearExpenses
            .GroupBy(t => new { t.TransactionCategoryId, Month = t.Date.ToLocal().Month })
            .Select(g => new
            {
                CategoryId = g.Key.TransactionCategoryId,
                Month      = g.Key.Month,
                Total      = g.Sum(t => t.Amount)
            })
            .ToList();

        // ── 3b. Weekly spending — separate targeted query, only if needed ─────
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

        // ── 4. Build progress list ──────────────────────────────────────────────
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
                        .Where(s => s.Month == requestLocalMonth);

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

            // Local-date period end for THIS budget's period type — reuses
            // the same UTC boundaries already computed above, just converted
            // back to local for human-terms day counting.
            var periodEndLocal = budget.Period switch
            {
                Domain.Enums.BudgetPeriod.Weekly => weekEnd.ToLocal().Date,
                Domain.Enums.BudgetPeriod.Yearly => yearEnd.ToLocal().Date,
                _ => monthEnd.ToLocal().Date // Monthly, and the default
            };

            // +1 makes this inclusive of today — if today IS the last day
            // of the period, there's still one day of spending room left,
            // not zero. Floored at 1 so this never divides by zero even if
            // the period has technically already ended.
            var daysRemaining = Math.Max(1, (periodEndLocal - todayLocal).Days + 1);

            var remaining = budget.Amount - spentAmount;
            var dailyAllowance = remaining > 0 ? remaining / daysRemaining : 0m;

            progressList.Add(new BudgetProgressDto
            {
                BudgetId              = budget.Id,
                TransactionCategoryId = budget.TransactionCategoryId,
                CategoryName          = budget.TransactionCategory?.Name,
                BudgetAmount          = budget.Amount,
                SpentAmount           = spentAmount,
                Period                = budget.Period,
                StartDate             = budget.StartDate,
                EndDate               = budget.EndDate,
                DaysRemainingInPeriod = daysRemaining,
                DailyAllowance        = dailyAllowance
            });
        }

        return Result<List<BudgetProgressDto>>.Success(
            progressList
                .OrderByDescending(p => p.PercentageUsed)
                .ToList());
    }
}
