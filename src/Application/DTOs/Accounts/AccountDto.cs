using Domain.Enums;

namespace Application.DTOs.Accounts;

public class AccountDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal Balance { get; set; }
    public string AccountCategoryName { get; set; } = string.Empty;
    public bool IsLiability { get; set; }
    public AccountType AccountType { get; set; }

    // At most one of these is populated, matching AccountType.
    public CreditCardDetailsDto? CreditCardDetails { get; set; }
    public LoanDetailsDto? LoanDetails { get; set; }
    public BankAccountDetailsDto? BankAccountDetails { get; set; }
}