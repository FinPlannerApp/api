using Domain.Enums;

namespace Domain.Entities;

public class RecurringTransaction : BaseEntity
{
    public required string UserId { get; set; }
    
    public int AccountId { get; set; }
    public Account Account { get; set; } = null!;

    public int? TransactionCategoryId { get; set; }
    public TransactionCategory? TransactionCategory { get; set; }

    public required string Description { get; set; }
    public decimal Amount { get; set; }
    public TransactionType Type { get; set; }
    
    public RecurrenceFrequency Frequency { get; set; }

    /// <summary>
    /// Bitmask of selected weekdays, only meaningful when Frequency == Custom
    /// (e.g. Monday | Wednesday | Friday). Null/None for all other frequencies.
    /// </summary>
    public RecurrenceDayOfWeek? CustomDays { get; set; }

    public DateTime StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    
    public DateTime NextProcessDate { get; set; }
    public bool IsActive { get; set; } = true;
    /// <summary>Set while the user has paused this (inactive but expected to come back). Null for cancelled/finished items.</summary>
    public DateTime? PausedAt { get; set; }
    public DateTime? LastProcessedDate { get; set; }

    /// <summary>
    /// Distinguishes a must-pay obligation (rent, EMI, insurance) from a
    /// regular recurring transaction (a subscription you could cancel
    /// without real consequence). Purely a user-set classification —
    /// the system doesn't infer this, since it has no way to actually
    /// know which recurring payments are truly essential.
    /// </summary>
    public bool IsObligation { get; set; } = false;

    /// <summary>
    /// Null for a normal recurring transaction. When set, this recurring
    /// transaction represents an EMI payment for the referenced loan
    /// account — AccountId is the PAYING account (e.g. a bank account),
    /// this is the LOAN account. When the job processes an occurrence
    /// with this set, it routes through AccountService.MakeLoanPaymentAsync
    /// (interest/principal split) instead of a plain expense.
    /// </summary>
    public int? LinkedLoanAccountId { get; set; }
    public Account? LinkedLoanAccount { get; set; }

    /// <summary>
    /// When set, this is a SIP / regular contribution: AccountId is the funding account and each occurrence
    /// is a transfer into this investment account (never an expense).
    /// </summary>
    public int? LinkedInvestmentAccountId { get; set; }
    public Account? LinkedInvestmentAccount { get; set; }

    public Subscription? Subscription { get; set; }
}
