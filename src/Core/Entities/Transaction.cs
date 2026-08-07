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

    /// <summary>
    /// Null for every ordinary transaction. Set to a shared value on BOTH
    /// legs of a transfer (the Expense on the source account, the Income
    /// on the destination) — links the pair together and, critically, is
    /// what every income/expense AGGREGATION filters out. Type still
    /// correctly says Income/Expense for balance-adjustment purposes;
    /// this field is purely about which totals a transaction should count
    /// toward, kept deliberately separate from Type so balance math never
    /// needs to change.
    /// </summary>
    public Guid? TransferGroupId { get; set; }

    public int? MerchantId { get; set; }
    public Merchant? Merchant { get; set; }
}