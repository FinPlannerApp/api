namespace Domain.Enums;

/// <summary>
/// Separate from AccountCategory.IsLiability (which continues to drive
/// balance-sign behavior — net worth exclusion, transfer balance-check
/// exemptions — exactly as it already does). AccountType is purely about
/// which detail fields/UI apply: Bank shows interest fields, CreditCard
/// shows limit/due-date fields, Loan shows EMI fields, Cash/Other show
/// none. A category could theoretically have AccountType=CreditCard and
/// IsLiability=false (e.g. a secured card backed by a deposit) — the two
/// are independently set, not derived from each other.
/// </summary>
public enum AccountType
{
    Bank = 0,
    CreditCard = 1,
    Loan = 2,
    Cash = 3,
    Other = 4
}

public enum InterestFrequency
{
    Monthly = 0,
    Quarterly = 1,
    Yearly = 2,
    Daily = 3
}
