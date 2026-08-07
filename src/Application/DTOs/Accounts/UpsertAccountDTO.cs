namespace Application.DTOs.Accounts;

public class UpsertAccountDto
{
    public int? Id { get; set; }
    public required string Name { get; set; }
    public decimal Balance { get; set; }
    public int AccountCategoryId { get; set; }
    public string? Purpose { get; set; }

    // Frontend sends whichever ONE of these is relevant based on the
    // selected category's AccountType — the other two stay null. The
    // service clears any now-irrelevant detail record if the account's
    // category type changes (e.g. was a Loan, user recategorizes it as
    // a Bank account — the stale LoanDetails row gets removed).
    public CreditCardDetailsDto? CreditCardDetails { get; set; }
    public LoanDetailsDto? LoanDetails { get; set; }
    public BankAccountDetailsDto? BankAccountDetails { get; set; }
}