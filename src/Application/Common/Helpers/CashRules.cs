using System.Linq.Expressions;
using Domain.Entities;
using Domain.Enums;

namespace Application.Common.Helpers;

/// <summary>Single definition of which accounts are spendable / emergency / held for others.</summary>
public static class CashRules
{
    /// <summary>Money you can spend: Bank/Cash by default, any non-liability account marked Spendable.</summary>
    public static readonly Expression<Func<Account, bool>> IsSpendable = a =>
        !a.AccountCategory.IsLiability && (
            a.CashRole == CashRole.Spendable ||
            (a.CashRole == CashRole.Auto &&
             (a.AccountCategory.AccountType == AccountType.Bank || a.AccountCategory.AccountType == AccountType.Cash)));

    /// <summary>Liquid savings that count toward emergency coverage (spendable cash + dedicated emergency accounts).</summary>
    public static readonly Expression<Func<Account, bool>> CountsForEmergencyCover = a =>
        !a.AccountCategory.IsLiability && (
            a.CashRole == CashRole.EmergencyFund || a.CashRole == CashRole.Spendable ||
            (a.CashRole == CashRole.Auto &&
             (a.AccountCategory.AccountType == AccountType.Bank || a.AccountCategory.AccountType == AccountType.Cash)));

    /// <summary>Custodial money is not yours: excluded from net worth.</summary>
    public static readonly Expression<Func<Account, bool>> IsOwnMoney = a => a.CashRole != CashRole.HeldForOthers;

    // In-memory equivalents for already-loaded entities (AccountCategory must be loaded).
    public static bool Spendable(Account a) =>
        !a.AccountCategory.IsLiability && (a.CashRole == CashRole.Spendable ||
        (a.CashRole == CashRole.Auto && (a.AccountCategory.AccountType is AccountType.Bank or AccountType.Cash)));
}
