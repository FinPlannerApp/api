using Domain.Enums;

namespace Application.DTOs.Transactions;

public class TransactionDto
{
    public int Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public DateTime Date { get; set; }
    public TransactionType Type { get; set; }
    public int AccountId { get; set; }
    public string? AccountName { get; set; }
    public string? CategoryName { get; set; }
}