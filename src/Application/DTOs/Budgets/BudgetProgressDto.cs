using Domain.Enums;

namespace Application.DTOs.Budgets;

public class BudgetProgressDto
{
    public int BudgetId { get; set; }
    public int? TransactionCategoryId { get; set; }
    public string? CategoryName { get; set; }
    public decimal BudgetAmount { get; set; }
    public decimal SpentAmount { get; set; }
    public decimal RemainingAmount => BudgetAmount - SpentAmount;
    public decimal PercentageUsed => BudgetAmount > 0 ? (SpentAmount / BudgetAmount) * 100 : 0;
    public BudgetPeriod Period { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime? EndDate { get; set; }

    /// <summary>
    /// Days left in the current period, counted in LOCAL calendar terms and
    /// inclusive of today (e.g. if today is the last day of the month, this
    /// is 1, not 0 — there's still a day of spending room left).
    /// Set by the query handler, not computed here — needs "today" and the
    /// period's local end date, neither of which the DTO has on its own.
    /// </summary>
    public int DaysRemainingInPeriod { get; set; }

    /// <summary>
    /// RemainingAmount ÷ DaysRemainingInPeriod. Zero (not negative) once
    /// the budget is already exceeded — "how much can I safely spend today"
    /// doesn't have a meaningful negative answer.
    /// </summary>
    public decimal DailyAllowance { get; set; }
}
