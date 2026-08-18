namespace Domain.Entities.Split;

public enum SplitGroupStatus { Active = 0, Locked = 1, Settled = 2, Archived = 3 }
public enum SplitType { Equal = 0, Exact = 1, Percentage = 2, Shares = 3 }
public enum SettlementMethod { Upi = 0, Cash = 1, BankTransfer = 2, Other = 3 }
public enum SettlementStatus { Pending = 0, Completed = 1, AwaitingConfirmation = 2 }

public class SplitGroup : BaseEntity
{
    public required string Name { get; set; }
    public required string CreatedByUserId { get; set; } // plain string, same convention as everywhere else
    public string Currency { get; set; } = "INR";
    public SplitGroupStatus Status { get; set; } = SplitGroupStatus.Active;

    /// <summary>
    /// A cryptographically random, unguessable token for the public,
    /// read-only shareable link — deliberately NOT the group's own Id,
    /// so links can't be enumerated by guessing sequential IDs.
    /// </summary>
    public required string ShareToken { get; set; }

    public ICollection<SplitGroupMember> Members { get; set; } = new List<SplitGroupMember>();
    public ICollection<SplitExpense> Expenses { get; set; } = new List<SplitExpense>();
    public ICollection<SplitSettlement> Settlements { get; set; } = new List<SplitSettlement>();
}

public class SplitGroupMember : BaseEntity
{
    public int SplitGroupId { get; set; }
    public SplitGroup SplitGroup { get; set; } = null!;

    public required string Name { get; set; }

    /// <summary>
    /// Null for a member who's just a name (the common case for a trip
    /// with people who don't have FinPlanner accounts). Set to a real
    /// FinPlanner UserId if this member IS the creator, or later, an
    /// actual linked account — plain string, no FK, same as every UserId
    /// field elsewhere in this app.
    /// </summary>
    public string? LinkedUserId { get; set; }

    public string? UpiId { get; set; }

    public ICollection<SplitExpensePayer> PaidExpenses { get; set; } = new List<SplitExpensePayer>();
    public ICollection<SplitExpenseParticipant> OwedExpenses { get; set; } = new List<SplitExpenseParticipant>();
}

public class SplitExpense : BaseEntity
{
    public int SplitGroupId { get; set; }
    public SplitGroup SplitGroup { get; set; } = null!;

    public required string Description { get; set; }
    public decimal Amount { get; set; }
    public DateTime Date { get; set; }
    public string? Category { get; set; } // plain string, deliberately not a shared TransactionCategory FK — full isolation
    public SplitType SplitType { get; set; }

    public ICollection<SplitExpensePayer> Payers { get; set; } = new List<SplitExpensePayer>();
    public ICollection<SplitExpenseParticipant> Participants { get; set; } = new List<SplitExpenseParticipant>();
}

/// <summary>
/// Who actually paid for an expense, and how much — separate from
/// Participants (who OWES a share), since the two are independent.
/// Supports multiple payers per expense (e.g. a hotel bill split across
/// two people's cards).
/// </summary>
public class SplitExpensePayer : BaseEntity
{
    public int SplitExpenseId { get; set; }
    public SplitExpense SplitExpense { get; set; } = null!;

    public int SplitGroupMemberId { get; set; }
    public SplitGroupMember SplitGroupMember { get; set; } = null!;

    public decimal AmountPaid { get; set; }
}

/// <summary>
/// Who owes a share of an expense, and how much. The actual "obligation"
/// record — Amount here is what THIS member's split works out to, after
/// whatever SplitType calculation (equal division, exact entry,
/// percentage, or shares) produced it.
/// </summary>
public class SplitExpenseParticipant : BaseEntity
{
    public int SplitExpenseId { get; set; }
    public SplitExpense SplitExpense { get; set; } = null!;

    public int SplitGroupMemberId { get; set; }
    public SplitGroupMember SplitGroupMember { get; set; } = null!;

    public decimal ShareAmount { get; set; }

    /// <summary>
    /// Set once this share has been imported as a real personal
    /// transaction — a plain int, no FK constraint into Transactions,
    /// same "soft reference" convention as every UserId field in this
    /// schema. Prevents importing the same share twice; doesn't create
    /// a hard database dependency in either direction.
    /// </summary>
    public int? ImportedTransactionId { get; set; }
}

public class SplitSettlement : BaseEntity
{
    public int SplitGroupId { get; set; }
    public SplitGroup SplitGroup { get; set; } = null!;

    public int FromMemberId { get; set; }
    public SplitGroupMember FromMember { get; set; } = null!;

    public int ToMemberId { get; set; }
    public SplitGroupMember ToMember { get; set; } = null!;

    public decimal Amount { get; set; }
    public SettlementMethod Method { get; set; }

    /// <summary>
    /// Snapshot of the UPI ID actually used at the time of this
    /// settlement — a member can change their UPI ID later, but
    /// historical settlements should keep showing what was actually
    /// used, not silently rewrite history to the current value.
    /// </summary>
    public string? UpiIdUsed { get; set; }

    public required string PaymentReference { get; set; }
    public SettlementStatus Status { get; set; } = SettlementStatus.Pending;
    public DateTime? CompletedAt { get; set; }
}
