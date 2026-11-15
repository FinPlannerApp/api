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
    Other = 4,
    /// <summary>Mutual funds, stocks, FD/RD, PPF, gold… Counts toward net worth (at current value if set), never toward spendable cash.</summary>
    Investment = 5
}

public enum InvestmentKind
{
    MutualFund = 0,
    Stock = 1,
    FixedDeposit = 2,
    RecurringDeposit = 3,
    Ppf = 4,
    Epf = 5,
    Gold = 6,
    Other = 7,
    Nps = 8,
    Bond = 9,
    Etf = 10,
    RealEstate = 11,
    Crypto = 12,
    PostOffice = 13
}

public enum InterestFrequency
{
    Monthly = 0,
    Quarterly = 1,
    Yearly = 2,
    Daily = 3,
    HalfYearly = 4
}

public enum InsuranceKind { Health = 0, Term = 1, Life = 2, Motor = 3, Home = 4, Travel = 5, PersonalAccident = 6, CriticalIllness = 7, Other = 8 }

/// <summary>How often the premium is paid. Single = one-off premium (no next due date).</summary>
public enum InsuranceFrequency { Monthly = 0, Quarterly = 1, HalfYearly = 2, Yearly = 3, Single = 4 }
