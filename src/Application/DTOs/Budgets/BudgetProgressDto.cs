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

    /// <summary>Alert level in effect for this budget: its own override, else the user's default (50–100).</summary>
    public int AlertThresholdPercent { get; set; } = 80;
    public bool HasThresholdOverride { get; set; }

    /// <summary>Local calendar start/end of the period being measured (monthly budgets honour the user's month-start day).</summary>
    public DateTime PeriodStart { get; set; }
    public DateTime PeriodEnd { get; set; }
    public int DaysTotal { get; set; }
    public int DaysElapsed { get; set; }

    /// <summary>Where a perfectly even spender would be today: DaysElapsed ÷ DaysTotal × 100.</summary>
    public decimal ExpectedPercent => DaysTotal > 0 ? Math.Round((decimal)DaysElapsed / DaysTotal * 100, 1) : 0;

    /// <summary>Spend by period end if the current daily pace continues.</summary>
    public decimal ProjectedSpend => DaysElapsed > 0 ? Math.Round(SpentAmount / DaysElapsed * DaysTotal, 2) : SpentAmount;

    /// <summary>safe, alert (≥ threshold), limit (≥ 100 %), over (&gt; 100 %).</summary>
    public string Status => PercentageUsed > 100 ? "over"
        : PercentageUsed >= 100 ? "limit"
        : PercentageUsed >= AlertThresholdPercent ? "alert"
        : "safe";
}
