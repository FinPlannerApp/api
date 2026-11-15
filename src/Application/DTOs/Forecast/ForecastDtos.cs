namespace Application.DTOs.Forecast;

public class MonthPointDto
{
    public string Month { get; set; } = "";          // yyyy-MM
    public decimal Income { get; set; }
    public decimal Spending { get; set; }
    public decimal? SavingsRatePercent { get; set; }
}

public class CategoryTypicalDto { public string Name { get; set; } = ""; public decimal Typical { get; set; } }

public class NextMonthDto
{
    public string Label { get; set; } = "";
    public decimal ExpectedIncome { get; set; }
    public bool IncomeFromHistory { get; set; }
    public decimal FixedBills { get; set; }           // recurring bills, premiums, EMIs
    public decimal Investing { get; set; }            // SIPs
    public decimal VariableTypical { get; set; }
    public decimal VariableLow { get; set; }
    public decimal VariableHigh { get; set; }
    public decimal LeftTypical { get; set; }
    public decimal LeftLow { get; set; }              // with the high variable spend
    public decimal LeftHigh { get; set; }
    public int MonthsOfData { get; set; }
    public string Confidence { get; set; } = "low";   // low | ok
    public List<CategoryTypicalDto> TopCategories { get; set; } = new();
}

public class RunwayDto
{
    public decimal StartBalance { get; set; }
    public decimal LowestBalance { get; set; }
    public DateTime LowestOn { get; set; }
    public DateTime? ShortOn { get; set; }            // first day the balance is projected below zero
    public int HorizonDays { get; set; }
    public decimal DailyVariable { get; set; }
}

public class InsightDto
{
    public string Kind { get; set; } = "";            // pace | category | anomaly | savings
    public string Severity { get; set; } = "info";    // info | watch
    public string Title { get; set; } = "";
    public string Detail { get; set; } = "";
}

public class BalancePointDto { public string Month { get; set; } = ""; public decimal Balance { get; set; } }

public class ForecastDto
{
    public List<MonthPointDto> History { get; set; } = new();
    public NextMonthDto NextMonth { get; set; } = new();
    public RunwayDto? Runway { get; set; }
    public List<InsightDto> Insights { get; set; } = new();
    public List<BalancePointDto> LiquidBalance { get; set; } = new();
}

// ── cards ──
public class CardSpendMonthDto { public string Month { get; set; } = ""; public decimal Spend { get; set; } }
public class NameAmountDto { public string Name { get; set; } = ""; public decimal Amount { get; set; } }

public class CardDto
{
    public int AccountId { get; set; }
    public string Name { get; set; } = "";
    public decimal? Limit { get; set; }
    public decimal Outstanding { get; set; }
    public decimal? UtilisationPercent { get; set; }
    public decimal? RemainingBill { get; set; }
    public DateTime? DueDate { get; set; }
    public decimal? InterestRate { get; set; }
    public decimal AvgMonthlySpend { get; set; }
    public int Purchases90d { get; set; }
    public List<CardSpendMonthDto> Months { get; set; } = new();
    public List<NameAmountDto> TopCategories { get; set; } = new();
    public List<decimal> Weekday { get; set; } = new();     // Mon..Sun, last 90 days
    public NameAmountDto? Biggest { get; set; }
}

public class CardsOverviewDto
{
    public List<CardDto> Cards { get; set; } = new();
    public decimal TotalLimit { get; set; }
    public decimal TotalOutstanding { get; set; }
    public decimal? UtilisationPercent { get; set; }
}

// ── accounts ──
public class AccountFlowMonthDto { public string Month { get; set; } = ""; public decimal In { get; set; } public decimal Out { get; set; } }

public class AccountDashItemDto
{
    public int AccountId { get; set; }
    public string Name { get; set; } = "";
    public decimal Balance { get; set; }
    public decimal LowestIn90Days { get; set; }
    public DateTime? LowestOn { get; set; }
    public decimal InterestLast12Months { get; set; }
    public List<AccountFlowMonthDto> Months { get; set; } = new();
}

public class AccountsDashboardDto
{
    public List<AccountDashItemDto> Accounts { get; set; } = new();
    public decimal TotalBalance { get; set; }
}
