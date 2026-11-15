using Domain.Enums;

namespace Domain.Entities;

/// <summary>"If a new transaction's description matches, fill in this category / merchant / these tags." Only empty fields are filled.</summary>
public class TransactionRule : BaseEntity
{
    public required string UserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string MatchText { get; set; } = string.Empty;
    /// <summary>0 contains, 1 starts with, 2 equals.</summary>
    public int MatchMode { get; set; }
    public TransactionType? TypeFilter { get; set; }
    public decimal? MinAmount { get; set; }
    public decimal? MaxAmount { get; set; }
    public int? TransactionCategoryId { get; set; }
    public int? MerchantId { get; set; }
    /// <summary>Packed like Transaction.Tags ("|a|b|").</summary>
    public string? Tags { get; set; }
    /// <summary>Lower runs first; the first matching rule wins.</summary>
    public int Priority { get; set; } = 100;
    public bool IsActive { get; set; } = true;
}
