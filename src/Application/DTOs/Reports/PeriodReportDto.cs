namespace Application.DTOs.Reports;

/// <summary>One month or one whole year, shown on screen first and exported on demand.</summary>
public class PeriodReportDto
{
    /// <summary>"month" or "year".</summary>
    public string Mode { get; set; } = "month";
    public string Label { get; set; } = string.Empty;
    public int Year { get; set; }
    public int? Month { get; set; }

    public decimal Income { get; set; }
    public decimal Expenses { get; set; }
    public decimal Net { get; set; }
    /// <summary>Net / Income as a percentage; 0 when there was no income.</summary>
    public decimal SavingsRate { get; set; }
    public decimal NetWorthNow { get; set; }

    public List<PeriodCategoryDto> Categories { get; set; } = new();

    /// <summary>One row per month. Filled for a year; a single row for a month.</summary>
    public List<PeriodMonthDto> Months { get; set; } = new();
}

public class PeriodCategoryDto
{
    public string Name { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public decimal Percent { get; set; }
}

public class PeriodMonthDto
{
    public int Month { get; set; }
    public string Label { get; set; } = string.Empty;
    public decimal Income { get; set; }
    public decimal Expenses { get; set; }
    public decimal Net { get; set; }
}
