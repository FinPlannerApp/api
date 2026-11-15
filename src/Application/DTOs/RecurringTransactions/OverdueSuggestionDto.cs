namespace Application.DTOs.RecurringTransactions;

/// <summary>
/// An overdue obligation that a recent transaction suggests was already paid but never marked as paid.
/// Read-only: nothing changes until the user confirms.
/// </summary>
public class OverdueSuggestionDto
{
    /// <summary>CreditCard | Loan | Recurring.</summary>
    public string Source { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string AccountName { get; set; } = string.Empty;
    public int? AccountId { get; set; }
    public int? CreditCardBillId { get; set; }
    public DateTime DueDate { get; set; }
    public decimal Amount { get; set; }

    /// <summary>high | medium.</summary>
    public string Confidence { get; set; } = "medium";
    /// <summary>Plain-language why, for example "A payment of 20,000 on 12 Aug on the card covers this."</summary>
    public string Reason { get; set; } = string.Empty;

    public int CandidateTransactionId { get; set; }
    public DateTime CandidateDate { get; set; }
    public decimal CandidateAmount { get; set; }
    public string CandidateDescription { get; set; } = string.Empty;
    public string CandidateAccountName { get; set; } = string.Empty;

    /// <summary>True for card statements: one tap marks the statement as paid.</summary>
    public bool CanMarkPaid { get; set; }
}
