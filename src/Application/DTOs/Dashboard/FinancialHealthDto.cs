namespace Application.DTOs.Dashboard;

public class FinancialHealthDto
{
    public int Score { get; set; } // 0-100
    public string Status { get; set; } = string.Empty;
    public List<FinancialHealthInsightDto> Insights { get; set; } = new();
    public decimal SavingsRate { get; set; }
    public decimal BudgetAdherence { get; set; }

    // 7 components, each showing its own points earned vs. max possible —
    // this is what actually lets someone see WHY their score is what it
    // is, not just the single final number.
    public List<FinancialHealthComponentDto> Components { get; set; } = new();
}

public class FinancialHealthComponentDto
{
    public string Name { get; set; } = string.Empty;
    public int PointsEarned { get; set; }
    public int MaxPoints { get; set; }
    public string Explanation { get; set; } = string.Empty;
}

public class FinancialHealthInsightDto
{
    public string Message { get; set; } = string.Empty;
    public string Type { get; set; } = "info";
    public string? CategoryName { get; set; }
}
