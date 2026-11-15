namespace Application.DTOs.Budgets;

/// <summary>Where a budget's spend went, split by category / sub-category, for the current period.</summary>
public class BudgetBreakdownDto
{
    public int BudgetId { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal BudgetAmount { get; set; }
    public decimal TotalSpent { get; set; }
    /// <summary>What the rows are: "sub-categories" (a category budget) or "categories" (an overall budget).</summary>
    public string Level { get; set; } = "categories";
    public List<BudgetBreakdownRowDto> Rows { get; set; } = new();
}

public class BudgetBreakdownRowDto
{
    public int? CategoryId { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal Spent { get; set; }
    public int TransactionCount { get; set; }
    /// <summary>Share of the budget's total spend (0–100).</summary>
    public decimal ShareOfSpend { get; set; }
    /// <summary>Share of the budget amount (0–100+), so a single row can show how much of the limit it used.</summary>
    public decimal PercentOfBudget { get; set; }
    public bool HasSubCategories { get; set; }
}
