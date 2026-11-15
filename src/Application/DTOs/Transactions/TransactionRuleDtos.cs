using Domain.Enums;

namespace Application.DTOs.Transactions;

public class TransactionRuleDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string MatchText { get; set; } = string.Empty;
    public int MatchMode { get; set; }
    public TransactionType? TypeFilter { get; set; }
    public decimal? MinAmount { get; set; }
    public decimal? MaxAmount { get; set; }
    public int? TransactionCategoryId { get; set; }
    public string? CategoryName { get; set; }
    public int? MerchantId { get; set; }
    public string? MerchantName { get; set; }
    public List<string> Tags { get; set; } = new();
    public int Priority { get; set; }
    public bool IsActive { get; set; }
}

public class UpsertTransactionRuleDto
{
    public int? Id { get; set; }
    public string? Name { get; set; }
    public string? MatchText { get; set; }
    public int MatchMode { get; set; }
    public TransactionType? TypeFilter { get; set; }
    public decimal? MinAmount { get; set; }
    public decimal? MaxAmount { get; set; }
    public int? TransactionCategoryId { get; set; }
    public int? MerchantId { get; set; }
    public List<string>? Tags { get; set; }
    public int Priority { get; set; } = 100;
    public bool IsActive { get; set; } = true;
}

public class RuleMatchRequestDto
{
    public string? Description { get; set; }
    public decimal Amount { get; set; }
    public TransactionType Type { get; set; }
}

public class RuleMatchDto
{
    public int RuleId { get; set; }
    public string RuleName { get; set; } = string.Empty;
    public int? TransactionCategoryId { get; set; }
    public int? MerchantId { get; set; }
    public List<string> Tags { get; set; } = new();
}
