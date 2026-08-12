namespace Application.DTOs.Accounts;

public class RecordCreditCardBillDto
{
    public int AccountId { get; set; }
    public decimal BillAmount { get; set; }
    public decimal? MinimumDue { get; set; }
    public DateTime? DueDate { get; set; } // optional override — falls back to CreditCardDetails.DueDate if not given
}

public class CreditCardBillResultDto
{
    public decimal RecordedBillAmount { get; set; }

    /// <summary>
    /// What CreditCardStatementCalculator computes from logged
    /// transactions alone — this is the number that DOESN'T know about
    /// interest or fees the bank added.
    /// </summary>
    public decimal ComputedFromTransactions { get; set; }

    /// <summary>
    /// RecordedBillAmount minus ComputedFromTransactions, floored at
    /// zero. The actual point of this feature — approximately how much
    /// of the real bill is interest/fees/charges that weren't
    /// individually logged as transactions. Approximate, not exact: it
    /// also silently absorbs any transactions you forgot to log, not
    /// just genuine bank charges. Worth treating as a signal to
    /// investigate, not a precise number.
    /// </summary>
    public decimal ImpliedInterestAndFees { get; set; }

    public DateTime StatementDate { get; set; }
}
