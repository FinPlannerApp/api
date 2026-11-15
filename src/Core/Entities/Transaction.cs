using Domain.Enums;

namespace Domain.Entities;

public class Transaction : BaseEntity
{
    public required string UserId { get; set; }
    public required string Description { get; set; }
    public decimal Amount { get; set; }
    public DateTime Date { get; set; }
    public TransactionType Type { get; set; }

    public int AccountId { get; set; }
    public Account Account { get; set; } = null!; // Navigation property

    public int? TransactionCategoryId { get; set; }
    public TransactionCategory? TransactionCategory { get; set; } // Navigation property
    public string DataHash { get; set; } = string.Empty;

    /// <summary>
    /// Null for every ordinary transaction. Set to a shared value on BOTH
    /// legs of a transfer (the Expense on the source account, the Income
    /// on the destination) — links the pair together and, critically, is
    /// what every income/expense AGGREGATION filters out. Type still
    /// correctly says Income/Expense for balance-adjustment purposes;
    /// this field is purely about which totals a transaction should count
    /// toward, kept deliberately separate from Type so balance math never
    /// needs to change.
    /// </summary>
    public Guid? TransferGroupId { get; set; }

    /// <summary>Ties together the rows created by ONE loan payment (principal legs and the interest expense), so
    /// deleting the payment removes exactly those rows. Null on older rows and every non-loan transaction.</summary>
    public Guid? PaymentGroupId { get; set; }

    public int? MerchantId { get; set; }
    public Merchant? Merchant { get; set; }

    /// <summary>Free-text note (up to 500 characters).</summary>
    public string? Notes { get; set; }

    /// <summary>Tags stored lower-case and pipe-delimited with both ends delimited, e.g. "|trip|work|", so a tag can be matched exactly with LIKE '%|trip|%'. Use TransactionTags to read and write.</summary>
    public string? Tags { get; set; }

    /// <summary>
    /// True for a correction created by AdjustBalanceAsync, never set by
    /// anything else. Exists purely for clear labeling/display/future
    /// filtering — the actual exclusion from income/expense totals comes
    /// from TransferGroupId being set, which already has every existing
    /// aggregation site checking for it.
    /// </summary>
    public bool IsBalanceAdjustment { get; set; } = false;

    /// <summary>
    /// True for an initial balance record created on account creation or backfilled,
    /// never set by anything else. Auditable starting point for transaction history
    /// and Brought Forward calculations.
    /// </summary>
    public bool IsOpeningBalance { get; set; } = false;

    /// <summary>
    /// Explicit classification, defaulting to Normal for every existing
    /// transaction. This is what lets the lifecycle-protection logic
    /// know which transactions have a genuine paired leg that
    /// needs protecting (Transfer, LoanPrincipal) versus which are
    /// self-contained singletons that just happen to also carry a
    /// TransferGroupId for exclusion purposes (BalanceAdjustment).
    /// </summary>
    public TransactionKind Kind { get; set; } = TransactionKind.Normal;

    /// <summary>Set only by the recurring job: which recurring item (and which scheduled occurrence)
    /// produced this row. A partial unique index makes posting the same occurrence twice impossible.</summary>
    public int? RecurringTransactionId { get; set; }
    public DateTime? RecurringOccurrenceDate { get; set; }

    /// <summary>Hash of the bank's reference number for a row that came from an imported statement.
    /// Unique per account, so re-importing the same statement can never add the row twice.</summary>
    public string? ImportRef { get; set; }
    /// <summary>When this entry was matched against (or created from) a bank statement.</summary>
    public DateTime? ReconciledAt { get; set; }
}
