namespace Application.DTOs.Accounts;

public class CreditCardBreakdownDto
{
    public decimal TotalOutstanding { get; set; }

    /// <summary>
    /// What was actually owed as of the most recent statement close —
    /// this is what a real bill would show, and what MinimumDue/DueDate
    /// actually apply to.
    /// </summary>
    public decimal StatementOutstanding { get; set; }

    /// <summary>
    /// Spending that's happened SINCE the last statement closed — real
    /// debt, but not yet due, and won't appear on a bill until next cycle.
    /// </summary>
    public decimal UnbilledOutstanding { get; set; }

    public DateTime MostRecentStatementDate { get; set; }
    public decimal? MinimumDueAmount { get; set; }
    public DateTime? DueDate { get; set; }
}
