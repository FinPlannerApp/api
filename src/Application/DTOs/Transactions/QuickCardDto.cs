using Domain.Enums;

namespace Application.DTOs.Transactions;

/// <summary>One of the user's habitual entries ("Bus ₹16, Transport, Cash"), offered as a one-tap card in Bulk add.</summary>
public class QuickCardDto
{
    public string Description { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public TransactionType Type { get; set; }
    public int? TransactionCategoryId { get; set; }
    public int AccountId { get; set; }
    /// <summary>How many times this exact entry was recorded in the look-back window.</summary>
    public int Count { get; set; }
    public DateTime LastUsed { get; set; }
}
