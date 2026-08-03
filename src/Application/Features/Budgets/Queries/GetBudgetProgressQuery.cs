using Application.Common.Models;
using Application.Contracts;
using Application.DTOs.Budgets;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Application.Features.Budgets.Queries;

public record GetBudgetProgressQuery(string UserId, DateTime RequestDate)
    : IRequest<Result<List<BudgetProgressDto>>>;

/// <summary>
/// Returns budget utilisation progress for the requesting user.
///
/// KEY FIX: The original fetched accountIds INSIDE the foreach loop,
/// causing N+1 DB queries (one per budget). With 10 budgets that was
/// 20+ round-trips. Now accountIds are fetched ONCE before the loop.
///
/// Also fetches all spending data in a SINGLE grouped query rather than
/// one SumAsync per budget, reducing DB round-trips from O(N) to O(1).
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

        // ── 2. Fetch account IDs ONCE (was: inside the loop — N+1 bug) ────────
        var accountIds = await _context.Accounts
            .AsNoTracking()
            .Where(a => a.UserId == request.UserId && !a.IsDeleted)
            .Select(a => a.Id)
            .ToListAsync(cancellationToken);

        if (!accountIds.Any())
            return Result<List<BudgetProgressDto>>.Success(new List<BudgetProgressDto>());

        // ── 3. Single grouped query for ALL spending data ─────────────────────
        // Fetch expense totals by category for both monthly and yearly windows
        // in ONE DB round-trip, then join in-memory.
        var spendingByCategory = await _context.Transactions
            .AsNoTracking()
            .Where(t => accountIds.Contains(t.AccountId)
                     && !t.IsDeleted
                     && t.Type == Domain.Enums.TransactionType.Expense
                     && t.Date >= yearStart      // wide window covers both monthly + yearly
                     && t.Date <= yearEnd)
            .GroupBy(t => new { t.TransactionCategoryId, t.Date.Month })
            .Select(g => new
            {
                CategoryId = g.Key.TransactionCategoryId,
                Month      = g.Key.Month,
                Total      = g.Sum(t => t.Amount)
            })
            .ToListAsync(cancellationToken);

        // ── 4. Build progress list from in-memory data ────────────────────────
        var progressList = new List<BudgetProgressDto>();

        foreach (var budget in activeBudgets)
        {
            decimal spentAmount;

            if (budget.Period == Domain.Enums.BudgetPeriod.Monthly)
            {
                // Filter to the current month's data
                var relevantRows = spendingByCategory
                    .Where(s => s.Month == request.RequestDate.Month);

                spentAmount = budget.TransactionCategoryId.HasValue
                    ? relevantRows
                        .Where(s => s.CategoryId == budget.TransactionCategoryId.Value)
                        .Sum(s => s.Total)
                    : relevantRows.Sum(s => s.Total);  // overall budget
            }
            else // Yearly
            {
                // All months in the year
                spentAmount = budget.TransactionCategoryId.HasValue
                    ? spendingByCategory
                        .Where(s => s.CategoryId == budget.TransactionCategoryId.Value)
                        .Sum(s => s.Total)
                    : spendingByCategory.Sum(s => s.Total);
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

        // Most over-budget first
        return Result<List<BudgetProgressDto>>.Success(
            progressList
                .OrderByDescending(p => p.PercentageUsed)
                .ToList());
    }
}
