namespace Domain.Entities;

/// <summary>
/// Log the reasoning behind a financial decision BEFORE making it — "why
/// am I buying this," "why am I skipping this" — then optionally come
/// back later and record how it actually turned out. The value here is
/// entirely in the user's own reflection; nothing about this is computed
/// or judged by the system.
/// </summary>
public class DecisionJournalEntry : BaseEntity
{
    public required string UserId { get; set; }
    public required string Title { get; set; }
    public required string Reasoning { get; set; }
    public decimal? Amount { get; set; }
    public DateTime DecisionDate { get; set; }

    // Both null until the user comes back and reflects — not required
    // at creation time, since the whole point is you often don't know
    // the outcome yet when you're making the decision.
    public string? Outcome { get; set; }
    public DateTime? OutcomeRecordedAt { get; set; }
}
