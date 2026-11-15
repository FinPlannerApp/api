namespace Domain.Entities;

/// <summary>Written when a new expense takes a budget over its alert threshold (Level 1) or over its limit (Level 2).</summary>
public class BudgetAlertEvent : BaseEntity
{
    public string UserId { get; set; } = string.Empty;
    public int BudgetId { get; set; }
    public int? TransactionId { get; set; }
    public int Level { get; set; }
    public int ThresholdPercent { get; set; }
    public decimal PercentUsed { get; set; }
    public decimal SpentAmount { get; set; }
    public decimal BudgetAmount { get; set; }
    public string? CategoryName { get; set; }
}
