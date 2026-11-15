using System.Text.Json.Serialization;
using Domain.Enums;

namespace Application.DTOs.Accounts;

public class InvestmentHoldingDto
{
    [JsonPropertyName("accountId")] public int AccountId { get; set; }
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
    [JsonPropertyName("kind")] public InvestmentKind Kind { get; set; }
    [JsonPropertyName("symbol")] public string? Symbol { get; set; }
    [JsonPropertyName("notes")] public string? Notes { get; set; }
    /// <summary>Money put in minus money taken out, from the ledger.</summary>
    [JsonPropertyName("netInvested")] public decimal NetInvested { get; set; }
    /// <summary>Latest manual valuation, or null if never valued.</summary>
    [JsonPropertyName("currentValue")] public decimal? CurrentValue { get; set; }
    [JsonPropertyName("valuedOn")] public DateTime? ValuedOn { get; set; }
    [JsonPropertyName("isStale")] public bool IsStale { get; set; }
    [JsonPropertyName("gain")] public decimal Gain { get; set; }
    [JsonPropertyName("gainPercent")] public decimal? GainPercent { get; set; }
    /// <summary>Annualised return in percent, when it can be computed (at least 30 days of history and a valuation).</summary>
    [JsonPropertyName("xirrPercent")] public double? XirrPercent { get; set; }
    [JsonPropertyName("monthlySip")] public decimal MonthlySip { get; set; }
    [JsonPropertyName("units")] public decimal? Units { get; set; }
    [JsonPropertyName("autoValuation")] public bool AutoValuation { get; set; }
    [JsonPropertyName("lastPrice")] public decimal? LastPrice { get; set; }
    [JsonPropertyName("lastPriceDate")] public DateTime? LastPriceDate { get; set; }
    [JsonPropertyName("lastPriceError")] public string? LastPriceError { get; set; }
    [JsonPropertyName("isArchived")] public bool IsArchived { get; set; }
}

public class SipDto
{
    [JsonPropertyName("recurringId")] public int RecurringId { get; set; }
    [JsonPropertyName("description")] public string Description { get; set; } = string.Empty;
    [JsonPropertyName("amount")] public decimal Amount { get; set; }
    [JsonPropertyName("frequency")] public RecurrenceFrequency Frequency { get; set; }
    [JsonPropertyName("nextDate")] public DateTime NextDate { get; set; }
    [JsonPropertyName("fundingAccountName")] public string FundingAccountName { get; set; } = string.Empty;
    [JsonPropertyName("investmentAccountId")] public int InvestmentAccountId { get; set; }
    [JsonPropertyName("investmentName")] public string InvestmentName { get; set; } = string.Empty;
}

public class AllocationSliceDto
{
    [JsonPropertyName("kind")] public InvestmentKind Kind { get; set; }
    [JsonPropertyName("value")] public decimal Value { get; set; }
    [JsonPropertyName("percent")] public decimal Percent { get; set; }
}

public class InvestmentOverviewDto
{
    [JsonPropertyName("totalNetInvested")] public decimal TotalNetInvested { get; set; }
    [JsonPropertyName("totalValue")] public decimal TotalValue { get; set; }
    [JsonPropertyName("totalGain")] public decimal TotalGain { get; set; }
    [JsonPropertyName("totalGainPercent")] public decimal? TotalGainPercent { get; set; }
    [JsonPropertyName("portfolioXirrPercent")] public double? PortfolioXirrPercent { get; set; }
    [JsonPropertyName("monthlySipTotal")] public decimal MonthlySipTotal { get; set; }
    [JsonPropertyName("holdings")] public List<InvestmentHoldingDto> Holdings { get; set; } = new();
    [JsonPropertyName("allocation")] public List<AllocationSliceDto> Allocation { get; set; } = new();
    [JsonPropertyName("sips")] public List<SipDto> Sips { get; set; } = new();
}

public class CreateInvestmentDto
{
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
    [JsonPropertyName("kind")] public InvestmentKind Kind { get; set; }
    /// <summary>Amount already invested (cost) — recorded as the opening balance. 0 if you will fund it by transfer.</summary>
    [JsonPropertyName("investedSoFar")] public decimal InvestedSoFar { get; set; }
    [JsonPropertyName("currentValue")] public decimal? CurrentValue { get; set; }
    [JsonPropertyName("symbol")] public string? Symbol { get; set; }
    [JsonPropertyName("units")] public decimal? Units { get; set; }
    [JsonPropertyName("autoValuation")] public bool AutoValuation { get; set; }
}

public class SetInvestmentValuationDto
{
    [JsonPropertyName("accountId")] public int AccountId { get; set; }
    [JsonPropertyName("kind")] public InvestmentKind? Kind { get; set; }
    /// <summary>Ignored (not required) when autoValuation is on and units + symbol are given.</summary>
    [JsonPropertyName("currentValue")] public decimal? CurrentValue { get; set; }
    [JsonPropertyName("units")] public decimal? Units { get; set; }
    [JsonPropertyName("autoValuation")] public bool? AutoValuation { get; set; }
    [JsonPropertyName("valuedOn")] public DateTime? ValuedOn { get; set; }
    [JsonPropertyName("symbol")] public string? Symbol { get; set; }
    [JsonPropertyName("notes")] public string? Notes { get; set; }
}
