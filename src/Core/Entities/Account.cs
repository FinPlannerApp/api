namespace Domain.Entities;

public class Account : BaseEntity
{
    public required string Name { get; set; }
    public decimal Balance { get; set; }
    public required string UserId { get; set; }

    public int AccountCategoryId { get; set; }
    public AccountCategory AccountCategory { get; set; } = null!; // Navigation property
    public string DataHash { get; set; } = string.Empty;

    public ICollection<Transaction> Transactions { get; set; } = new List<Transaction>();

    // At most ONE of these is populated at any time, matching whichever
    // AccountType the account's current category represents. Nullable —
    // a Cash/Other account (or a Bank/CreditCard/Loan account where the
    // user hasn't filled in details yet) has all three null.
    public CreditCardDetails? CreditCardDetails { get; set; }
    public LoanDetails? LoanDetails { get; set; }
    public BankAccountDetails? BankAccountDetails { get; set; }
}