namespace Domain.Entities;

public class AccountCategory : BaseEntity
{
    public required string Name { get; set; }
    public required string UserId { get; set; }

    /// <summary>
    /// True for categories representing liabilities (credit cards, loans, EMIs).
    /// Used by DashboardService to exclude liability balances from net worth,
    /// replacing the previous hardcoded string match on category.Name which
    /// only caught categories named EXACTLY "Credit Card" or "Loan" — missing
    /// common real-world names like "Home Loan", "Car EMI", "Visa Card", etc.
    /// Defaults to false (asset). User toggles this when creating/editing a category.
    /// </summary>
    public bool IsLiability { get; set; } = false;
}