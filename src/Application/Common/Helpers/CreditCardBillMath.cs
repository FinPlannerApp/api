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

        var paid = await context.CreditCardPayments
            .Where(p => p.CreditCardAccountId == bill.AccountId && !p.IsDeleted &&
                        (p.CreditCardBillId == bill.Id ||
                         (p.CreditCardBillId == null && p.Date >= bill.StatementDate)))
            .SumAsync(p => (decimal?)p.Amount, ct) ?? 0m;

        return Math.Max(0m, bill.BillAmount - paid);
    }
}
