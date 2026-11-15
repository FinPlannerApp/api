namespace Domain.Enums;

public enum TransactionKind
{
    Normal = 0,
    Transfer = 1,
    BalanceAdjustment = 2,
    LoanPrincipal = 3,
    LoanInterest = 4,
    OpeningBalance = 5,
    /// <summary>Savings interest credited by the bank, posted from the Interest-due list.</summary>
    InterestCredit = 6,
    /// <summary>Realised gain/loss booked on an investment account when money is withdrawn. Counts in net invested, not in XIRR cash flows.</summary>
    InvestmentGain = 7
}
