namespace Application.DTOs.DecisionJournal;

public class DecisionJournalEntryDto
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Reasoning { get; set; } = string.Empty;
    public decimal? Amount { get; set; }
    public DateTime DecisionDate { get; set; }
    public string? Outcome { get; set; }
    public DateTime? OutcomeRecordedAt { get; set; }
}

public class UpsertDecisionJournalEntryDto
{
    public int? Id { get; set; }
    public required string Title { get; set; }
    public required string Reasoning { get; set; }
    public decimal? Amount { get; set; }
    public DateTime DecisionDate { get; set; }
}

public class RecordOutcomeDto
{
    public int Id { get; set; }
    public required string Outcome { get; set; }
}
