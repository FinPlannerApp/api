using Application.Contracts;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Application.Common.Helpers;

/// <summary>
/// One definition of "how much of this statement bill is still unpaid", shared by
/// the dashboard and the upcoming-obligations list so they can never disagree.
/// A payment counts toward a bill when it is explicitly linked to it, or when it
/// carries no bill link and was made on/after the statement date — the same rule
/// the payment flow itself uses.
/// </summary>
public static class CreditCardBillMath
{
    public static async Task<decimal> RemainingAsync(
        IApplicationDbContext context, CreditCardBill bill, CancellationToken ct = default)
    {
        if (bill.IsPaid) return 0m;
        if (bill.RemainingAmount.HasValue) return bill.RemainingAmount.Value;   // stored (PH1-03): no summing on every read

        var next = await NextStatementAsync(context, bill, ct);
        var paid = await context.CreditCardPayments
            .Where(p => p.CreditCardAccountId == bill.AccountId && !p.IsDeleted &&
                        (p.CreditCardBillId == bill.Id ||
                         (p.CreditCardBillId == null && p.Date >= bill.StatementDate &&
                          (next == null || p.Date < next))))
            .SumAsync(p => (decimal?)p.Amount, ct) ?? 0m;

        return Math.Max(0m, bill.BillAmount - paid);
    }

    /// <summary>
    /// Recomputes the stored PaidAmount / RemainingAmount of every bill on one card with the attribution rule above.
    /// Call it AFTER payments or bills were saved, then save again. Cheap: a card has a handful of bills.
    /// </summary>
    public static async Task RecomputeStoredAsync(IApplicationDbContext context, int accountId, CancellationToken ct = default)
    {
        var bills = await context.CreditCardBills
            .Where(b => b.AccountId == accountId && !b.IsDeleted)
            .OrderBy(b => b.StatementDate)
            .ToListAsync(ct);
        if (bills.Count == 0) return;
        var payments = await context.CreditCardPayments.AsNoTracking()
            .Where(p => p.CreditCardAccountId == accountId && !p.IsDeleted)
            .Select(p => new { p.Amount, p.Date, p.CreditCardBillId })
            .ToListAsync(ct);

        for (var i = 0; i < bills.Count; i++)
        {
            var b = bills[i];
            DateTime? next = i + 1 < bills.Count ? bills[i + 1].StatementDate : null;
            var paid = payments
                .Where(p => p.CreditCardBillId == b.Id ||
                            (p.CreditCardBillId == null && p.Date >= b.StatementDate && (next == null || p.Date < next)))
                .Sum(p => p.Amount);
            b.PaidAmount = paid;
            b.RemainingAmount = b.IsPaid ? 0m : Math.Max(0m, b.BillAmount - paid);
        }
    }

    /// <summary>Every bill on the card that still has an unpaid amount, oldest first, with what remains on each.</summary>
    public static async Task<List<(CreditCardBill Bill, decimal Remaining)>> UnpaidBillsAsync(
        IApplicationDbContext context, int accountId, CancellationToken ct = default)
    {
        var bills = await context.CreditCardBills
            .Where(b => b.AccountId == accountId && !b.IsDeleted && !b.IsPaid)
            .OrderBy(b => b.StatementDate)
            .ToListAsync(ct);
        var result = new List<(CreditCardBill, decimal)>();
        foreach (var b in bills)
        {
            var remaining = await RemainingAsync(context, b, ct);
            if (remaining > 0m) result.Add((b, remaining));
        }
        return result;
    }

    /// <summary>
    /// Statement date of the next NEWER bill on the same card, or null for the latest bill. An unlinked payment
    /// belongs to the statement period it was made in (from this statement up to the next one) — before, it was
    /// counted against every older bill too, so one payment made older bills look settled.
    /// </summary>
    public static async Task<DateTime?> NextStatementAsync(
        IApplicationDbContext context, CreditCardBill bill, CancellationToken ct = default)
    {
        return await context.CreditCardBills
            .Where(b => b.AccountId == bill.AccountId && !b.IsDeleted && b.StatementDate > bill.StatementDate)
            .OrderBy(b => b.StatementDate)
            .Select(b => (DateTime?)b.StatementDate)
            .FirstOrDefaultAsync(ct);
    }
}
