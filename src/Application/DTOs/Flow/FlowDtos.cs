namespace Application.DTOs.Flow;

public class ObligationDto
{
    public string Key { get; set; } = "";            // rt-12 / card-5 / loan-7
    public string Source { get; set; } = "";         // recurring | card | loan
    public int? RecurringId { get; set; }
    public string Name { get; set; } = "";
    public string Group { get; set; } = "";          // Bills | Debt | Investing
    public decimal Amount { get; set; }
    public string FrequencyLabel { get; set; } = "";
    public DateTime? NextDate { get; set; }
    public string? FromAccount { get; set; }
    public string? ToAccount { get; set; }           // investment or loan being paid
    public bool MustPay { get; set; }
    public bool IsPaused { get; set; }
    public bool IsInsurance { get; set; }            // managed from the Insurance page
    public bool CanChange { get; set; }              // pause / resume / cancel allowed here
    public decimal MonthlyEquivalent { get; set; }
}

public class ObligationEventDto
{
    public DateTime Date { get; set; }
    public string Name { get; set; } = "";
    public string Group { get; set; } = "";
    public decimal Amount { get; set; }
    public bool MustPay { get; set; }
}

public class ObligationsOverviewDto
{
    public List<ObligationDto> Items { get; set; } = new();
    public List<ObligationEventDto> Upcoming { get; set; } = new();   // next 60 days
    public decimal MustPayMonthly { get; set; }
    public decimal InvestingMonthly { get; set; }
    public decimal OptionalMonthly { get; set; }
    public decimal Next30DaysOutflow { get; set; }
}

public class ObligationIdDto { public int RecurringId { get; set; } }

public class InvestmentWithdrawDto
{
    public int AccountId { get; set; }                 // the investment
    public int DestinationAccountId { get; set; }      // where the money landed
    public DateTime Date { get; set; }
    public decimal ReceivedAmount { get; set; }        // exactly what reached the bank
    public decimal? GrossAmount { get; set; }          // before tax / charges, if known
    public string? Notes { get; set; }
}

public class InvestmentWithdrawPreviewDto
{
    public decimal Gross { get; set; }
    public decimal Charges { get; set; }
    public decimal CostRedeemed { get; set; }
    public decimal Gain { get; set; }                  // negative = loss
    public decimal ValueBefore { get; set; }
    public decimal ValueAfter { get; set; }
    public bool Estimated { get; set; }
    public string? Warning { get; set; }
}
