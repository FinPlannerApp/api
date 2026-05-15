using Domain.Enums;

namespace Domain.Entities;

public class Transaction : BaseEntity
{
    public required string UserId { get; set; }
    public required string Description { get; set; }
    public decimal Amount { get; set; }
    public DateTime Date { get; set; }
    public TransactionType Type { get; set; }

    public int AccountId { get; set; }
    public Account Account { get; set; } = null!; // Navigation property

    public int? TransactionCategoryId { get; set; }
    public TransactionCategory? TransactionCategory { get; set; } // Navigation property
    public string DataHash { get; set; } = string.Empty;
}