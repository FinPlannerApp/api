namespace Domain.Enums;

/// <summary>
/// How an account counts toward the dashboard's spendable-cash maths.
/// Auto keeps the historical behaviour (Bank and Cash accounts are spendable).
/// </summary>
public enum CashRole
{
    Auto = 0,
    Spendable = 1,
    EmergencyFund = 2,
    HeldForOthers = 3
}
