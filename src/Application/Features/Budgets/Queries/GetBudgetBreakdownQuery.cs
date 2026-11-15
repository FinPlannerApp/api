using Application.Common.Helpers;
using Application.Common.Models;
using Application.Contracts;
using Application.DTOs.Budgets;
using Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Application.Features.Budgets.Queries;

public record GetBudgetBreakdownQuery(string UserId, int BudgetId, DateTime RequestDate, int? CategoryId)
    : IRequest<Result<BudgetBreakdownDto>>;

/// <summary>
/// Splits one budget's current-period spend by category. A category budget is split into its sub-categories
/// (plus what was filed directly under the main category); an overall budget is split into main categories.
/// Passing CategoryId drills one level further into that category's sub-categories.
/// </summary>
public class GetBudgetBreakdownQueryHandler : IRequestHandler<GetBudgetBreakdownQuery, Result<BudgetBreakdownDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserSettingsService _settings;

    public GetBudgetBreakdownQueryHandler(IApplicationDbContext context, IUserSettingsService settings)
    {
        _context = context;
        _settings = settings;
    }

    public async Task<Result<BudgetBreakdownDto>> Handle(GetBudgetBreakdownQuery request, CancellationToken ct)
    {
        var budget = await _context.Budgets.AsNoTracking()
            .Include(b => b.TransactionCategory)
            .FirstOrDefaultAsync(b => b.Id == request.BudgetId && b.UserId == request.UserId && !b.IsDeleted, ct);
        if (budget == null)
            return Result.Failure<BudgetBreakdownDto>(new Error("Budget.NotFound", "Budget not found."));

        var startDay = (await _settings.GetGeneralAsync(request.UserId, ct)).MonthStartDay;
        var (start, end) = budget.Period switch
        {
            BudgetPeriod.Weekly => AppTimeZone.WeekBoundsUtc(request.RequestDate),
            BudgetPeriod.Yearly => AppTimeZone.YearBoundsUtc(request.RequestDate),
            _ => AppTimeZone.MonthBoundsUtc(request.RequestDate, startDay)
        };

        var accountIds = await _context.Accounts.AsNoTracking()
            .Where(a => a.UserId == request.UserId && !a.IsDeleted).Select(a => a.Id).ToListAsync(ct);

        var categories = await _context.TransactionCategories.AsNoTracking()
            .Where(c => c.UserId == request.UserId && !c.IsDeleted)
            .Select(c => new { c.Id, c.Name, c.ParentCategoryId }).ToListAsync(ct);
        var byId = categories.ToDictionary(c => c.Id);

        var spend = await _context.Transactions.AsNoTracking()
            .Where(t => accountIds.Contains(t.AccountId) && !t.IsDeleted && t.Type == TransactionType.Expense &&
                        t.TransferGroupId == null && t.Date >= start && t.Date <= end)
            .GroupBy(t => t.TransactionCategoryId)
            .Select(g => new { CategoryId = g.Key, Total = g.Sum(t => t.Amount), Count = g.Count() })
            .ToListAsync(ct);

        // The category whose children we split (drill-down > the budget's own category > none = overall).
        var focusId = request.CategoryId ?? budget.TransactionCategoryId;
        var focusName = focusId.HasValue && byId.TryGetValue(focusId.Value, out var f) ? f.Name : null;

        // In scope: everything the budget covers (the budget's category and its children, or everything).
        bool InScope(int? catId)
        {
            if (focusId.HasValue)
                return catId.HasValue && (catId == focusId || (byId.TryGetValue(catId.Value, out var c) && c.ParentCategoryId == focusId));
            return true;
        }

        var rows = new Dictionary<string, BudgetBreakdownRowDto>();
        foreach (var s in spend.Where(s => InScope(s.CategoryId)))
        {
            int? key; string name;
            if (!s.CategoryId.HasValue) { key = null; name = "Uncategorised"; }
            else if (focusId.HasValue)
            {
                // Under a focused category: each sub-category on its own; the category itself becomes "Directly in X".
                key = s.CategoryId;
                name = s.CategoryId == focusId ? $"Directly in {focusName}" : byId[s.CategoryId.Value].Name;
            }
            else
            {
                // Overall budget: roll sub-categories up into their main category.
                var c = byId.GetValueOrDefault(s.CategoryId.Value);
                key = c?.ParentCategoryId ?? s.CategoryId;
                name = key.HasValue && byId.TryGetValue(key.Value, out var top) ? top.Name : "Uncategorised";
            }
            var k = key?.ToString() ?? "none";
            if (!rows.TryGetValue(k, out var row))
                rows[k] = row = new BudgetBreakdownRowDto
                {
                    CategoryId = key, Name = name,
                    HasSubCategories = key.HasValue && !focusId.HasValue && categories.Any(c => c.ParentCategoryId == key)
                };
            row.Spent += s.Total;
            row.TransactionCount += s.Count;
        }

        var total = rows.Values.Sum(r => r.Spent);
        foreach (var r in rows.Values)
        {
            r.ShareOfSpend = total > 0 ? Math.Round(r.Spent / total * 100, 1) : 0;
            r.PercentOfBudget = budget.Amount > 0 ? Math.Round(r.Spent / budget.Amount * 100, 1) : 0;
        }

        return Result.Success(new BudgetBreakdownDto
        {
            BudgetId = budget.Id,
            Name = focusName ?? "All spending",
            BudgetAmount = budget.Amount,
            TotalSpent = total,
            Level = focusId.HasValue ? "sub-categories" : "categories",
            Rows = rows.Values.OrderByDescending(r => r.Spent).ToList()
        });
    }
}
