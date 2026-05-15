namespace Application.DTOs.Dashboard;

public class FinancialHealthDto
{
    public int Score { get; set; } // 0-100
    public string Status { get; set; } = string.Empty; // e.g., "Good", "Excellent", "Needs Improvement"
    public List<FinancialHealthInsightDto> Insights { get; set; } = new();
    public decimal SavingsRate { get; set; }
    public decimal BudgetAdherence { get; set; }
}

public class FinancialHealthInsightDto
{
    public string Message { get; set; } = string.Empty;
    public string Type { get; set; } = "info"; // info, success, warning
    public string? CategoryName { get; set; }
}
