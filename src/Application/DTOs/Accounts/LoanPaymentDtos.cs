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

/// <summary>
/// Moves a loan's next EMI due date forward WITHOUT creating any transaction or touching any
/// balance. For EMIs the bank already auto-debited (and the balance already reflects), so
/// recording a payment would take the money out twice.
/// </summary>
public class AdvanceLoanDueDateDto
{
    public int Months { get; set; } = 1;
}
