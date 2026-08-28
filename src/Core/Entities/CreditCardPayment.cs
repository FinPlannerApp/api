namespace Domain.Entities;

public enum CashbackType { Direct = 0, Indirect = 1 }

/// <summary>
/// One payment toward a credit card bill — a bill may be paid via
/// several of these, from different accounts, on different dates.
/// Separate from Transaction: this is the record of the payment EVENT
/// and its cashback context; the Transactions it generates are linked
/// by id for a full audit trail.
/// </summary>
public class CreditCardPayment : BaseEntity
{
    public required string UserId { get; set; }
    public int CreditCardAccountId { get; set; }
    public int PayingAccountId { get; set; }
    public decimal Amount { get; set; }
    public DateTime Date { get; set; }
    public int? CreditCardBillId { get; set; }
    public CreditCardBill? CreditCardBill { get; set; }

    public string? PaymentAppName { get; set; } // "GPay", "PhonePe", "Bank App", etc.

    public decimal? CashbackAmount { get; set; }
    public CashbackType? CashbackType { get; set; }

    /// <summary>
    /// Only meaningful when CashbackType is Direct — which account the
    /// cashback actually landed in. Null for Indirect (wallet) cashback,
    /// since there's no trackable account to attribute it to.
    /// </summary>
    public int? CashbackAccountId { get; set; }

    public decimal InterestPortion { get; set; }
    public decimal PrincipalPortion { get; set; }

    // Soft references to the transactions this payment generated — no
    // FK constraints, same convention as everywhere else in this app.
    public int? InterestTransactionId { get; set; }
    public int? PrincipalExpenseTransactionId { get; set; }
    public int? PrincipalIncomeTransactionId { get; set; }
    public int? CashbackTransactionId { get; set; }
}
