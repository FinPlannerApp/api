namespace Application.DTOs.Accounts;

public class PaymentAppWalletDto
{
    public required string PaymentAppName { get; set; }
    public decimal CurrentBalance { get; set; }
    public List<PaymentAppWalletLedgerEntryDto> RecentEntries { get; set; } = new();
}

public class PaymentAppWalletLedgerEntryDto
{
    public decimal Amount { get; set; }
    public string Type { get; set; } = string.Empty; // "Earned" or "Applied"
    public DateTime Date { get; set; }
    public int CreditCardPaymentId { get; set; }
}

