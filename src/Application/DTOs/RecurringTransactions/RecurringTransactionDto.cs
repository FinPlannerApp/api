using Domain.Enums;

namespace Application.DTOs.RecurringTransactions;

public class RecurringTransactionDto
{
    public int Id { get; set; }
    public int AccountId { get; set; }
    public string AccountName { get; set; } = string.Empty;
    public int? TransactionCategoryId { get; set; }
    public string? CategoryName { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public TransactionType Type { get; set; }
    public RecurrenceFrequency Frequency { get; set; }
    public RecurrenceDayOfWeek? CustomDays { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public DateTime NextProcessDate { get; set; }
    public bool IsActive { get; set; }
    public DateTime? LastProcessedDate { get; set; }
    public bool IsObligation { get; set; }
}
