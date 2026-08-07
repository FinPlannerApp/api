namespace Application.DTOs.RecurringTransactions;

public class UpcomingObligationDto
{
    public string Description { get; set; } = string.Empty;
    public string AccountName { get; set; } = string.Empty;

    // Null for credit cards — a card's actual bill amount varies month to
    // month and isn't known until the statement generates. MinimumDueAmount
    // is shown separately, explicitly labeled as a minimum, not guessed at
    // as if it were the full bill.
    public decimal? Amount { get; set; }
    public decimal? MinimumDueAmount { get; set; } // only populated for CreditCard source

    public DateTime DueDate { get; set; }

    // "Recurring" or "CreditCard" — lets the frontend show a different
    // icon/label per source without needing two separate API calls merged
    // client-side.
    public string Source { get; set; } = string.Empty;
}
