namespace Application.DTOs.Budgets;

/// <summary>How the user's budget alerts cluster over time: which categories, which weekdays, how far into the month.</summary>
public class BudgetAlertPatternsDto
{
    public int Months { get; set; }
    public int TotalAlerts { get; set; }
    public int Warnings { get; set; }
    public int Breaches { get; set; }
    /// <summary>Plain-language observations, most notable first.</summary>
    public List<string> Insights { get; set; } = new();
    public List<BudgetAlertCategoryPatternDto> Categories { get; set; } = new();
    /// <summary>Alerts per money-month, oldest first.</summary>
    public List<BudgetAlertMonthPointDto> ByMonth { get; set; } = new();
    /// <summary>Mon..Sun counts (index 0 = Monday).</summary>
    public int[] ByWeekday { get; set; } = new int[7];
    /// <summary>Early (days 1–10), mid (11–20) and late (21+) of the money-month.</summary>
    public int[] ByPartOfMonth { get; set; } = new int[3];
}

public class BudgetAlertCategoryPatternDto
{
    public string CategoryName { get; set; } = string.Empty;
    public int Warnings { get; set; }
    public int Breaches { get; set; }
    /// <summary>Distinct money-months in which this category hit its limit.</summary>
    public int MonthsBreached { get; set; }
    /// <summary>Average day of the money-month on which the limit was first reached (0 when it never was).</summary>
    public double AvgBreachDay { get; set; }
}

public class BudgetAlertMonthPointDto
{
    public string Label { get; set; } = string.Empty;
    public int Warnings { get; set; }
    public int Breaches { get; set; }
}
