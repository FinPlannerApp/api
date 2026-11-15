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
    public int? MerchantId { get; set; }
    public string? MerchantName { get; set; }
    public string? Notes { get; set; }
    public List<string> Tags { get; set; } = new();

    /// <summary>
    /// Null for every ordinary transaction. Non-null (shared with its
    /// paired leg) for a transfer — lets the frontend show a distinct
    /// "Transfer" indicator instead of a plain colored +/- amount, if you
    /// want that later. Not required for the core fix, just exposed so
    /// it's available.
    /// </summary>
    public Guid? TransferGroupId { get; set; }

    /// <summary>Set only on the response to a newly saved expense that pushed a budget past its alert level or limit.</summary>
    public List<Application.DTOs.Budgets.BudgetAlertDto>? BudgetAlerts { get; set; }
}
