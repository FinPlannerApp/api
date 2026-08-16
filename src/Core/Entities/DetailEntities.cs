namespace Domain.Entities;

/// <summary>
/// At most one of these exists per Account — enforced by a unique index on
/// AccountId at the DB level (see migration). Standard BaseEntity shape
/// (own Id, not a shared-PK 1:1 pattern) to stay consistent with every
/// other entity in this codebase.
/// </summary>
public class CreditCardDetails : BaseEntity
{
    public int AccountId { get; set; }
    public Account Account { get; set; } = null!;

    public decimal? CreditLimit { get; set; }
    public decimal? MinimumDueAmount { get; set; }
    public DateTime? DueDate { get; set; }
    public DateTime? StatementClosingDate { get; set; } // "Bill Date" in the UI — same concept, clearer label there

    /// <summary>Annual/membership fee, if any.</summary>
    public decimal? AnnualFee { get; set; }

    /// <summary>APR on unpaid balances.</summary>
    public decimal? InterestRate { get; set; }
}

public class LoanDetails : BaseEntity
{
    public int AccountId { get; set; }
    public Account Account { get; set; } = null!;

    public decimal? PrincipalAmount { get; set; }
    public decimal? InterestRate { get; set; }
    public decimal? EmiAmount { get; set; }
    public int? TenureMonths { get; set; }
    public DateTime? NextEmiDueDate { get; set; }
    public DateTime? StartDate { get; set; }

    /// <summary>
    /// Which account you plan to pay this loan's EMI from — optional,
    /// used only for the shortfall warning in Upcoming Obligations. Not
    /// required for the loan to function; a loan with this unset simply
    /// never shows a shortfall warning, since there's nothing to check
    /// the balance against.
    /// </summary>
    public int? DesignatedPayingAccountId { get; set; }
}

public class BankAccountDetails : BaseEntity
{
    public int AccountId { get; set; }
    public Account Account { get; set; } = null!;

    public decimal? InterestRate { get; set; }
    public Domain.Enums.InterestFrequency? InterestFrequency { get; set; }

    /// <summary>Minimum balance the bank requires — informational, no
    /// automatic penalty tracking, just surfaced so it's visible.</summary>
    public decimal? MinimumBalance { get; set; }
}
