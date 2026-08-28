using Domain.Entities;

namespace Application.DTOs.Accounts;

public class SinglePaymentEntryDto
{
    public int PayingAccountId { get; set; }
    public decimal Amount { get; set; }
    public DateTime Date { get; set; }
    public int? CreditCardBillId { get; set; }
    public string? PaymentAppName { get; set; }
    public decimal? CashbackAmount { get; set; }
    public CashbackType? CashbackType { get; set; }
    public int? CashbackAccountId { get; set; }
    public int? InterestCategoryId { get; set; }
}

public class MakeCreditCardPaymentBatchDto
{
    public int CreditCardAccountId { get; set; }
    public int? CreditCardBillId { get; set; }
    public List<SinglePaymentEntryDto> Payments { get; set; } = new();
}

public class SinglePaymentResultDto
{
    public decimal InterestPortion { get; set; }
    public decimal PrincipalPortion { get; set; }
    public decimal? CashbackAmount { get; set; }
    public CashbackType? CashbackType { get; set; }
}

public class CreditCardPaymentBatchResultDto
{
    public List<SinglePaymentResultDto> Payments { get; set; } = new();
    public decimal TotalPaid { get; set; }
    public decimal TotalInterestPaid { get; set; }
    public decimal TotalCashbackReceived { get; set; }
    public decimal DirectCashbackReceived { get; set; }
    public decimal IndirectCashbackReceived { get; set; }
    public decimal RemainingBalance { get; set; }
    public bool BillMarkedPaid { get; set; }
}
