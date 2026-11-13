namespace Application.DTOs.Accounts;

public class RecordCreditCardBillDto
{
    public int AccountId { get; set; }
    public decimal BillAmount { get; set; }
    public decimal? MinimumDue { get; set; }
    /// <summary>Optional: interest/fees included in the bill. The rest of any gap is booked as untracked card spend.</summary>
    public decimal? InterestAndFees { get; set; }
    /// <summary>Optional: other charges (late/annual fee, hidden charges, GST on them).</summary>
    public decimal? OtherCharges { get; set; }
    /// <summary>Optional: the statement (bill) date. Defaults to the card's most recent statement date.</summary>
    public DateTime? StatementDate { get; set; }
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

public class OutstandingBillDto
{
    public int Id { get; set; }
    public DateTime StatementDate { get; set; }
    public decimal BillAmount { get; set; }
    public decimal PaidAmount { get; set; }
    public decimal RemainingAmount { get; set; }
    public decimal? MinimumDue { get; set; }
    public DateTime? DueDate { get; set; }
}

public class CardPaymentHistoryDto
{
    public int Id { get; set; }
    public DateTime Date { get; set; }
    public decimal Amount { get; set; }
    public int PayingAccountId { get; set; }
    public string PayingAccountName { get; set; } = string.Empty;
    public string? PaymentAppName { get; set; }
    public decimal PrincipalPortion { get; set; }
    public decimal InterestPortion { get; set; }
    public decimal? CashbackAmount { get; set; }
    /// <summary>Direct | Indirect | StatementCredit | null</summary>
    public string? CashbackType { get; set; }
    public string? CashbackAccountName { get; set; }
    /// <summary>Payment-app wallet balance applied as a discount on this payment.</summary>
    public decimal WalletApplied { get; set; }
    /// <summary>Wallet cashback earned (indirect) on this payment.</summary>
    public decimal WalletEarned { get; set; }
}

public class CardStatementHistoryDto
{
    public int BillId { get; set; }
    public DateTime StatementDate { get; set; }
    public DateTime? DueDate { get; set; }
    public decimal BillAmount { get; set; }
    public decimal? MinimumDue { get; set; }
    public decimal InterestAndFees { get; set; }
    public decimal OtherCharges { get; set; }
    public decimal Paid { get; set; }
    public decimal Remaining { get; set; }
    public decimal CashbackTotal { get; set; }
    /// <summary>Paid | Partly paid | Unpaid</summary>
    public string Status { get; set; } = string.Empty;
    public List<CardPaymentHistoryDto> Payments { get; set; } = new();
}

public class CardHistoryDto
{
    public List<CardStatementHistoryDto> Statements { get; set; } = new();
    /// <summary>Payments made without choosing a statement.</summary>
    public List<CardPaymentHistoryDto> UnlinkedPayments { get; set; } = new();
}
