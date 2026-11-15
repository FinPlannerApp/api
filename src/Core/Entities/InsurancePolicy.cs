namespace Domain.Entities;

/// <summary>
/// A real-world insurance policy the user holds (health, term, motor…). It is a record with reminders,
/// not a ledger account: premiums are normal expenses (log them or use a recurring transaction).
/// Dates are calendar dates (stored as SQL "date"), compared with the app's local "today".
/// </summary>
public class InsurancePolicy : BaseEntity
{
    public required string UserId { get; set; }
    public int Kind { get; set; }                 // InsuranceKind
    public required string Name { get; set; }     // e.g. "Star Health Family Floater"
    public string? Insurer { get; set; }
    public string? PolicyNumber { get; set; }     // optional, user-supplied
    public string? InsuredPeople { get; set; }    // free text, e.g. "Self, spouse, 2 kids"
    public string? Nominee { get; set; }
    public decimal SumAssured { get; set; }       // cover amount
    public decimal Premium { get; set; }          // amount per payment
    public int PremiumFrequency { get; set; }     // InsuranceFrequency
    public DateTime? StartDate { get; set; }
    public DateTime? NextDueDate { get; set; }    // next premium due
    public DateTime? ExpiryDate { get; set; }     // policy end / renewal date
    public DateTime? WaitingPeriodEnds { get; set; }
    public string? Notes { get; set; }

    /// <summary>Account the premium is paid from (bank, cash or credit card). Null = you pay it yourself.</summary>
    public int? PayFromAccountId { get; set; }
    /// <summary>When true (and PayFromAccountId set) a recurring obligation posts the premium on each due date.</summary>
    public bool AutoRecord { get; set; }
    /// <summary>The recurring obligation that posts the premium; its next date IS the policy's next due date.</summary>
    public int? RecurringTransactionId { get; set; }
    public bool IsActive { get; set; } = true;
}
