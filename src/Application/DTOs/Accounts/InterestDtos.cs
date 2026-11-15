using System.Text.Json.Serialization;

namespace Application.DTOs.Accounts;

public class InterestDueDto
{
    [JsonPropertyName("accountId")] public int AccountId { get; set; }
    [JsonPropertyName("accountName")] public string AccountName { get; set; } = string.Empty;
    [JsonPropertyName("periodStart")] public DateTime PeriodStart { get; set; }
    [JsonPropertyName("periodEnd")] public DateTime PeriodEnd { get; set; }
    [JsonPropertyName("days")] public int Days { get; set; }
    [JsonPropertyName("averageBalance")] public decimal AverageBalance { get; set; }
    [JsonPropertyName("ratePercent")] public decimal RatePercent { get; set; }
    [JsonPropertyName("estimatedAmount")] public decimal EstimatedAmount { get; set; }
    [JsonPropertyName("autoPost")] public bool AutoPost { get; set; }
}

public class InterestOverviewDto
{
    [JsonPropertyName("due")] public List<InterestDueDto> Due { get; set; } = new();
    [JsonPropertyName("earnedThisMonth")] public decimal EarnedThisMonth { get; set; }
    [JsonPropertyName("earnedThisYear")] public decimal EarnedThisYear { get; set; }
    [JsonPropertyName("paidThisMonth")] public decimal PaidThisMonth { get; set; }
    [JsonPropertyName("paidThisYear")] public decimal PaidThisYear { get; set; }
}

public class PostInterestDto
{
    [JsonPropertyName("accountId")] public int AccountId { get; set; }
    /// <summary>Period end exactly as returned by interest-overview.</summary>
    [JsonPropertyName("periodEnd")] public DateTime PeriodEnd { get; set; }
    /// <summary>What the bank actually credited (may differ slightly from the estimate).</summary>
    [JsonPropertyName("amount")] public decimal Amount { get; set; }
    /// <summary>Set only by the background job.</summary>
    [JsonIgnore] public bool IsAuto { get; set; }
}
