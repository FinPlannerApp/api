namespace Domain.Enums;

public enum TransactionKind
{
    Normal = 0,
    Transfer = 1,
    BalanceAdjustment = 2,
    LoanPrincipal = 3,
    LoanInterest = 4,
    OpeningBalance = 5
}
