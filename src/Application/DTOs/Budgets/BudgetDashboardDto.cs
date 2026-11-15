using Application.DTOs.Settings;

namespace Application.DTOs.Budgets;

public class BudgetDashboardDto
{
    /// <summary>Every active budget with live progress, most-used first.</summary>
    public List<BudgetProgressDto> Budgets { get; set; } = new();

    // Headline numbers cover MONTHLY budgets only — summing weekly/yearly budgets into a monthly total would mislead.
    public decimal TotalBudgeted { get; set; }
    public decimal TotalSpent { get; set; }
    public decimal TotalRemaining => TotalBudgeted - TotalSpent;
    public decimal PercentUsed => TotalBudgeted > 0 ? Math.Round(TotalSpent / TotalBudgeted * 100, 1) : 0;
    public int SafeCount { get; set; }
    public int AlertCount { get; set; }
    public int OverCount { get; set; }

    public DateTime PeriodStart { get; set; }
    public DateTime PeriodEnd { get; set; }
    public int DaysLeft { get; set; }

    /// <summary>Last six months (oldest first): spend against monthly budgets vs. the budgeted amount.</summary>
    public List<BudgetHistoryPointDto> History { get; set; } = new();
    public List<BudgetAlertEventDto> RecentAlerts { get; set; } = new();
    public int AlertsThisPeriod { get; set; }

    public BudgetSettingsDto Settings { get; set; } = new();
    public int MonthStartDay { get; set; } = 1;
}

public class BudgetHistoryPointDto
{
    public string Label { get; set; } = string.Empty;
    public decimal Spent { get; set; }
    public decimal Budgeted { get; set; }
}

public class BudgetAlertEventDto
{
    public int BudgetId { get; set; }
    public string? CategoryName { get; set; }
    /// <summary>1 = reached alert level, 2 = reached the limit.</summary>
    public int Level { get; set; }
    public int ThresholdPercent { get; set; }
    public decimal PercentUsed { get; set; }
    public decimal SpentAmount { get; set; }
    public decimal BudgetAmount { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>Returned on a saved expense when it pushed a budget over its alert level or limit, so the UI can toast immediately.</summary>
public class BudgetAlertDto
{
    public int BudgetId { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public int Level { get; set; }
    public int ThresholdPercent { get; set; }
    public decimal PercentUsed { get; set; }
    public decimal SpentAmount { get; set; }
    public decimal BudgetAmount { get; set; }
}
