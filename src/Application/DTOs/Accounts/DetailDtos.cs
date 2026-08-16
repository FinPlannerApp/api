using Domain.Enums;

namespace Application.DTOs.Accounts;

public class CreditCardDetailsDto
{
    public decimal? CreditLimit { get; set; }
    public decimal? MinimumDueAmount { get; set; }
    public DateTime? DueDate { get; set; }
    public DateTime? StatementClosingDate { get; set; }
    public decimal? AnnualFee { get; set; }
    public decimal? InterestRate { get; set; }
}

public class LoanDetailsDto
{
    public decimal? PrincipalAmount { get; set; }
    public decimal? InterestRate { get; set; }
    public decimal? EmiAmount { get; set; }
    public int? TenureMonths { get; set; }
    public DateTime? NextEmiDueDate { get; set; }
    public DateTime? StartDate { get; set; }
    public int? DesignatedPayingAccountId { get; set; }
}

public class BankAccountDetailsDto
{
    public decimal? InterestRate { get; set; }
    public InterestFrequency? InterestFrequency { get; set; }
    public decimal? MinimumBalance { get; set; }
}
