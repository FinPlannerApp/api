namespace Application.Common.Helpers;

public class AmortizationScheduleEntry
{
    public int Month { get; set; }
    public DateTime Date { get; set; }
    public decimal OpeningBalance { get; set; }
    public decimal EmiAmount { get; set; }
    public decimal PrincipalComponent { get; set; }
    public decimal InterestComponent { get; set; }
    public decimal ClosingBalance { get; set; }
}

public static class LoanAmortization
{
    /// <summary>
    /// Splits ONE payment into its interest and principal components,
    /// given the outstanding balance at the time of payment. This is
    /// what actually gets used when recording a real payment — it uses
    /// the loan's REAL current outstanding balance, not a value read off
    /// a pre-computed static schedule, so it stays correct even if
    /// payments happen late, early, or in different amounts than a
    /// textbook schedule would assume.
    /// </summary>
    public static (decimal Interest, decimal Principal) CalculateSplit(
        decimal outstandingBalance, decimal annualRatePercent, decimal paymentAmount)
    {
        if (outstandingBalance <= 0)
            return (0, 0); // loan already fully paid off — nothing to split

        var monthlyRate = annualRatePercent / 100m / 12m;
        var interest = Math.Round(outstandingBalance * monthlyRate, 2);

        // Cap interest at the payment amount — without this, a payment
        // smaller than the interest due would produce a NEGATIVE
        // principal component, which would silently INCREASE the
        // outstanding balance instead of decreasing it. A genuinely
        // wrong-direction bug if left unguarded.
        interest = Math.Min(interest, paymentAmount);

        var principal = paymentAmount - interest;

        // Cap principal at the outstanding balance — handles the final
        // payment or a deliberate overpayment without going negative.
        principal = Math.Min(principal, outstandingBalance);

        return (interest, principal);
    }

    /// <summary>
    /// Projects a full month-by-month schedule FORWARD from whatever
    /// balance is passed in. Callers should pass the loan's REAL current
    /// outstanding balance (Math.Abs(account.Balance)), not the original
    /// PrincipalAmount — once any real payments have been recorded via
    /// CalculateSplit above, the original principal no longer reflects
    /// where the loan actually stands. Display/projection only — nothing
    /// here is stored.
    /// </summary>
    public static List<AmortizationScheduleEntry> GenerateSchedule(
        decimal currentOutstandingBalance, decimal annualRatePercent, decimal emiAmount,
        int maxMonths, DateTime startDate)
    {
        var schedule = new List<AmortizationScheduleEntry>();
        var balance = currentOutstandingBalance;
        var monthlyRate = annualRatePercent / 100m / 12m;

        for (int month = 1; month <= maxMonths && balance > 0.01m; month++)
        {
            var interest = Math.Round(balance * monthlyRate, 2);
            var principalComponent = Math.Min(emiAmount - interest, balance);

            // If EMI doesn't even cover the interest, the loan is
            // mathematically never paid off at this payment amount —
            // stop projecting rather than loop toward infinity or a
            // misleading, ever-growing balance.
            if (principalComponent <= 0)
                break;

            var closingBalance = balance - principalComponent;

            schedule.Add(new AmortizationScheduleEntry
            {
                Month = month,
                Date = startDate.AddMonths(month - 1),
                OpeningBalance = balance,
                EmiAmount = interest + principalComponent, // correctly smaller on the final, partial month
                PrincipalComponent = principalComponent,
                InterestComponent = interest,
                ClosingBalance = closingBalance
            });

            balance = closingBalance;
        }

        return schedule;
    }
}
