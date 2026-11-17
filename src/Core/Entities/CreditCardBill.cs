namespace Domain.Entities;

/// <summary>
/// The REAL, official amount from an actual credit card statement — as
/// opposed to CreditCardStatementCalculator's computed estimate, which
/// only reflects logged transactions and has no way to know about
/// interest, late fees, or GST the bank added. Recording a real bill
/// here is what lets those two numbers be compared against each other.
/// </summary>
public class CreditCardBill : BaseEntity
{
    public required string UserId { get; set; }
    public int AccountId { get; set; }
    public Account Account { get; set; } = null!;
    public DateTime StatementDate { get; set; }
    public decimal BillAmount { get; set; }
    public decimal? MinimumDue { get; set; }
    public DateTime? DueDate { get; set; }
    /// <summary>Interest/fees the user says are inside BillAmount (0 = unknown; the gap is treated as untracked spend).</summary>
    public decimal InterestAndFees { get; set; }
    /// <summary>Other non-purchase charges in the bill (late fee, annual fee, hidden charges, GST on those).</summary>
    public decimal OtherCharges { get; set; }
    public bool IsPaid { get; set; } = false;

    /// <summary>Stored total of payments that count toward this bill (kept up to date by CreditCardBillMath.RecomputeStoredAsync). Null until first computed.</summary>
    public decimal? PaidAmount { get; set; }
    /// <summary>Stored amount still unpaid (0 when IsPaid). Null until first computed.</summary>
    public decimal? RemainingAmount { get; set; }
}
