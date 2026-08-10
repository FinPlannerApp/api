namespace Application.DTOs.Dashboard;

public class DashboardSummaryDto
{
    public decimal NetWorth { get; set; }
    public decimal MonthlyIncome { get; set; }
    public decimal MonthlyExpenses { get; set; }
    public decimal BroughtForwardAmount { get; set; }
    public decimal RealAvailableCash { get; set; }

    /// <summary>
    /// Sum of StatementOutstanding across every credit card — what's
    /// actually due right now, not total outstanding including
    /// unbilled spending that isn't billed yet.
    /// </summary>
    public decimal ReservedForPayment { get; set; }
}
