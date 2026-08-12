using Application.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Application.Common.Helpers;

public class NetWorthResult
{
    public decimal TotalAssets { get; set; }
    public decimal TotalLiabilities { get; set; } // positive, for display — see below
    public decimal NetWorth { get; set; }
}

public static class NetWorthCalculator
{
    /// <summary>
    /// The one authoritative net worth calculation. Liability balances
    /// are already stored negative, so NetWorth is just the sum of every
    /// account — no filtering, no subtraction needed, the sign already
    /// does the work. TotalLiabilities is returned as a positive number
    /// specifically for display purposes (a report showing "Liabilities:
    /// -70000" under that label reads as confusing, even though the
    /// underlying number is correct) — NetWorth itself is computed from
    /// the raw signed values, not the display-friendly one.
    /// </summary>
    public static async Task<NetWorthResult> CalculateAsync(IApplicationDbContext context, string userId, CancellationToken cancellationToken = default)
    {
        var totalAssets = await context.Accounts
            .Where(a => a.UserId == userId && !a.AccountCategory.IsLiability)
            .SumAsync(a => a.Balance, cancellationToken);

        var totalLiabilitiesRaw = await context.Accounts
            .Where(a => a.UserId == userId && a.AccountCategory.IsLiability)
            .SumAsync(a => a.Balance, cancellationToken);

        return new NetWorthResult
        {
            TotalAssets = totalAssets,
            TotalLiabilities = Math.Abs(totalLiabilitiesRaw),
            NetWorth = totalAssets + totalLiabilitiesRaw
        };
    }
}
