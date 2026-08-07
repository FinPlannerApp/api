namespace Domain.Rules;

public interface IFinancialRule
{
    string RuleName { get; }
    string Description { get; }
    FinancialInsight Evaluate(RuleContext context);
}

public class RuleContext
{
    public required string UserId { get; set; }
    public decimal TotalIncome { get; set; }
    public decimal TotalExpense { get; set; }
    public decimal PreviousIncome { get; set; }
    public decimal PreviousExpense { get; set; }
    public decimal SavingsRate => TotalIncome == 0 ? 0 : (TotalIncome - TotalExpense) / TotalIncome;
    public decimal SubscriptionSpend { get; set; }
    public decimal BudgetLimit { get; set; }

    public decimal BaselineExpenseFourToSixMonthsAgo { get; set; }
    public decimal SpendingInThreeDaysAfterLargestIncome { get; set; }
    public decimal AverageDailySpendRestOfMonth { get; set; }
    public int OldestActiveSubscriptionMonths { get; set; }
}

public class FinancialInsight
{
    public required string Title { get; set; }
    public required string Message { get; set; }
    public InsightType Type { get; set; }
    public bool Triggered { get; set; }
}

public enum InsightType
{
    Positive,
    Warning,
    Danger,
    Info
}
