namespace Domain.Entities;

public enum PaymentAppWalletEntryType
{
    Earned = 0,  // indirect cashback credited to this app's balance
    Applied = 1  // balance used as a discount on a later payment
}

/// <summary>
/// One event affecting a payment app's wallet balance — an earn or a
/// use, always tied to the specific CreditCardPayment that caused it.
/// The current balance for an app is never stored directly; it's
/// always computed by summing these entries (Earned minus Applied),
/// the same way CreditCardBill.PaidAmount is computed from real
/// payments rather than kept as a separately maintained number.
/// </summary>
public class PaymentAppWalletLedgerEntry : BaseEntity
{
    public required string UserId { get; set; }
    public required string PaymentAppName { get; set; }

    // Always positive — Type determines whether it added to or drew
    // down the balance, rather than encoding direction in the sign.
    public decimal Amount { get; set; }

    public PaymentAppWalletEntryType Type { get; set; }

    public int CreditCardPaymentId { get; set; }
    public CreditCardPayment CreditCardPayment { get; set; } = null!;

    public DateTime Date { get; set; }
}

