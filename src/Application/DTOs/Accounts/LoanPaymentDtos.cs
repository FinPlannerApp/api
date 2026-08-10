namespace Application.DTOs.Accounts;

public class MakeLoanPaymentDto
{
    public int LoanAccountId { get; set; }
    public int PayingAccountId { get; set; }
    public decimal Amount { get; set; }
    public DateTime Date { get; set; }
}

public class LoanPaymentResultDto
{
    public decimal InterestPortion { get; set; }
    public decimal PrincipalPortion { get; set; }
    public decimal RemainingBalance { get; set; }
}

public class AmortizationScheduleDto
{
    public List<Application.Common.Helpers.AmortizationScheduleEntry> Schedule { get; set; } = new();
    public decimal CurrentOutstandingBalance { get; set; }
    public int EstimatedMonthsRemaining { get; set; }
    public decimal TotalInterestRemaining { get; set; }
}
