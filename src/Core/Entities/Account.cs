namespace Domain.Entities;

public class Account : BaseEntity
{
    public required string Name { get; set; }
    public decimal Balance { get; set; }
    public required string UserId { get; set; }

    public int AccountCategoryId { get; set; }
    public AccountCategory AccountCategory { get; set; } = null!; // Navigation property
    public string DataHash { get; set; } = string.Empty;

    /// <summary>
    /// Distinct from IsDeleted — an archived account (closed bank account,
    /// cancelled card) is meant to be KEPT, just retired from active use.
    /// Excluded from "select an account" dropdowns for new transactions,
    /// but still shows in the accounts list (visually marked) and keeps
    /// its full transaction history. IsDeleted means "I don't want this
    /// at all"; IsArchived means "I'm done with this but want to remember it."
    /// </summary>
    public bool IsArchived { get; set; } = false;

    /// <summary>
    /// Freeform, distinct from AccountCategory/AccountType — those
    /// classify WHAT KIND of account this is (Bank, Credit Card), this
    /// says WHAT IT'S FOR ("Emergency Fund", "Daily Spends", "House Down
    /// Payment"). Deliberately not an enum — the whole value here is
    /// user-specific intent, which a fixed list can't anticipate.
    /// </summary>
    public string? Purpose { get; set; }

    public ICollection<Transaction> Transactions { get; set; } = new List<Transaction>();

    // At most ONE of these is populated at any time, matching whichever
    // AccountType the account's current category represents. Nullable —
    // a Cash/Other account (or a Bank/CreditCard/Loan account where the
    // user hasn't filled in details yet) has all three null.
    public CreditCardDetails? CreditCardDetails { get; set; }
    public LoanDetails? LoanDetails { get; set; }
    public BankAccountDetails? BankAccountDetails { get; set; }
}