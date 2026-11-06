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

    /// <summary>Bank + cash account balances (what you hold).</summary>
    public decimal LiquidCash { get; set; }

    /// <summary>Total owed on credit cards today (billed + unbilled), as a positive number.</summary>
    public decimal CreditCardDebt { get; set; }

    /// <summary>Loan EMIs, recurring obligations and account charges due in the window that Real Available Cash does not yet reflect.</summary>
    public decimal UpcomingObligationsTotal { get; set; }

    /// <summary>Real Available Cash minus upcoming obligations. Can be negative.</summary>
    public decimal SafeToSpend { get; set; }

    public int ObligationWindowDays { get; set; } = 30;
}
