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
    public DateTime? LastProcessedDate { get; set; }

    /// <summary>
    /// Distinguishes a must-pay obligation (rent, EMI, insurance) from a
    /// regular recurring transaction (a subscription you could cancel
    /// without real consequence). Purely a user-set classification —
    /// the system doesn't infer this, since it has no way to actually
    /// know which recurring payments are truly essential.
    /// </summary>
    public bool IsObligation { get; set; } = false;

    public Subscription? Subscription { get; set; }
}
