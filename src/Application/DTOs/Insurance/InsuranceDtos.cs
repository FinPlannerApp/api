namespace Application.DTOs.Insurance;

public class InsurancePolicyDto
{
    public int Id { get; set; }
    public int Kind { get; set; }
    public string Name { get; set; } = "";
    public string? Insurer { get; set; }
    public string? PolicyNumber { get; set; }
    public string? InsuredPeople { get; set; }
    public string? Nominee { get; set; }
    public decimal SumAssured { get; set; }
    public decimal Premium { get; set; }
    public int PremiumFrequency { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? NextDueDate { get; set; }
    public DateTime? ExpiryDate { get; set; }
    public DateTime? WaitingPeriodEnds { get; set; }
    public string? Notes { get; set; }
    public bool IsActive { get; set; }
    public int? PayFromAccountId { get; set; }
    public string? PayFromAccountName { get; set; }
    public bool AutoRecord { get; set; }

    // Derived
    public decimal AnnualPremium { get; set; }
    public int? DaysToDue { get; set; }          // negative = overdue
    public int? DaysToExpiry { get; set; }       // negative = expired
    public bool InWaitingPeriod { get; set; }
    public DateTime? LastPaidOn { get; set; }
    public decimal TotalPaid { get; set; }
    public int PaymentsCount { get; set; }
}

public class UpsertInsurancePolicyDto
{
    public int? Id { get; set; }
    public int Kind { get; set; }
    public string Name { get; set; } = "";
    public string? Insurer { get; set; }
    public string? PolicyNumber { get; set; }
    public string? InsuredPeople { get; set; }
    public string? Nominee { get; set; }
    public decimal SumAssured { get; set; }
    public decimal Premium { get; set; }
    public int PremiumFrequency { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? NextDueDate { get; set; }
    public DateTime? ExpiryDate { get; set; }
    public DateTime? WaitingPeriodEnds { get; set; }
    public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;
    public int? PayFromAccountId { get; set; }
    public bool AutoRecord { get; set; }
}

public class InsuranceCoverByKindDto
{
    public int Kind { get; set; }
    public decimal SumAssured { get; set; }
    public decimal AnnualPremium { get; set; }
    public int Policies { get; set; }
}

public class InsuranceOverviewDto
{
    public List<InsurancePolicyDto> Policies { get; set; } = new();
    public decimal TotalAnnualPremium { get; set; }
    public decimal NextThirtyDaysPremium { get; set; }
    public List<InsuranceCoverByKindDto> ByKind { get; set; } = new();
}
