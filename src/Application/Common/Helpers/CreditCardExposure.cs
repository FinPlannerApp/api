using Application.Contracts;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Application.Common.Helpers;

/// <summary>
/// One definition of "how much do I owe on this card right now", shared by the
/// dashboard summary and the financial-health score.
/// Owed = the larger of (a) the tracked balance (negative = debt) and (b) the
/// still-unpaid part of the latest recorded statement bill. A bill that was
/// recorded without the matching charges being tracked leaves the balance at 0,
/// and previously that debt was invisible to Real Available Cash, Safe-to-Spend
/// and the health score even though it is listed under upcoming obligations.
/// </summary>
public static class CreditCardExposure
{
    public sealed record CardOwed(Account Card, decimal TrackedDebt, decimal Owed)
    {
        /// <summary>Debt present only as a recorded bill, not in the balance.</summary>
        public decimal Untracked => Owed - TrackedDebt;
    }

    public static async Task<List<CardOwed>> ComputeAsync(
        IApplicationDbContext context, IEnumerable<Account> cards, CancellationToken ct = default)
    {
        var result = new List<CardOwed>();
        foreach (var card in cards)
        {
            var tracked = Math.Max(0m, -card.Balance);
            var bill = await context.CreditCardBills
                .Where(b => b.AccountId == card.Id)
                .OrderByDescending(b => b.StatementDate)
                .FirstOrDefaultAsync(ct);
            var remaining = bill == null ? 0m : await CreditCardBillMath.RemainingAsync(context, bill, ct);
            result.Add(new CardOwed(card, tracked, Math.Max(tracked, remaining)));
        }
        return result;
    }
}
