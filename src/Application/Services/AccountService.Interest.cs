using Application.Common.Helpers;
using Application.Common.Models;
using Application.Contracts;
using Application.DTOs.Accounts;
using Domain.Entities;
using Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Application.Services;

/// <summary>Savings-account interest: suggests what each finished period earned and records what the bank credited.</summary>
public partial class AccountService
{
    private const int MaxInterestPeriodsListed = 12;

    private static int PeriodMonths(InterestFrequency f) => f switch
    {
        InterestFrequency.Quarterly => 3,
        InterestFrequency.HalfYearly => 6,
        InterestFrequency.Yearly => 12,
        _ => 1 // Monthly, and Daily (credited daily by banks, recorded here monthly)
    };

    /// <summary>Next credit date on or after <paramref name="d"/>: in a calendar-aligned month (month / quarter / half / year end),
    /// on the account's credit day (clamped to the month length) or the last day when none is set.</summary>
    private static DateTime PeriodEndOnOrAfter(DateTime d, int months, int? creditDay)
    {
        var idx = (d.Month - 1) / months;
        var endMonth = (idx + 1) * months;
        var year = d.Year;
        for (var guard = 0; guard < 4; guard++)
        {
            var dim = DateTime.DaysInMonth(year, endMonth);
            var cand = new DateTime(year, endMonth, creditDay.HasValue ? Math.Min(creditDay.Value, dim) : dim);
            if (cand >= d) return cand;
            endMonth += months;
            while (endMonth > 12) { endMonth -= 12; year++; }
        }
        return new DateTime(d.Year, endMonth, 1);
    }

    public async Task<Result<InterestOverviewDto>> GetInterestOverviewAsync(string userId)
    {
        var result = new InterestOverviewDto();
        var today = AppTimeZone.TodayLocal();

        var accounts = await _context.Accounts.AsNoTracking()
            .Include(a => a.AccountCategory)
            .Include(a => a.BankAccountDetails)
            .Where(a => a.UserId == userId && !a.IsArchived)
            .ToListAsync();

        var monthStart = new DateTime(today.Year, today.Month, 1).ToUtc();
        var yearStart = new DateTime(today.Year, 1, 1).ToUtc();

        // Totals actually recorded
        var earned = await _context.Transactions.AsNoTracking()
            .Where(t => t.UserId == userId && t.Kind == TransactionKind.InterestCredit && t.Date >= yearStart)
            .Select(t => new { t.Date, t.Amount }).ToListAsync();
        result.EarnedThisYear = earned.Sum(e => e.Amount);
        result.EarnedThisMonth = earned.Where(e => e.Date >= monthStart).Sum(e => e.Amount);
        var paid = await _context.Transactions.AsNoTracking()
            .Where(t => t.UserId == userId && t.Kind == TransactionKind.LoanInterest && t.Date >= yearStart)
            .Select(t => new { t.Date, t.Amount }).ToListAsync();
        result.PaidThisYear = paid.Sum(e => e.Amount);
        result.PaidThisMonth = paid.Where(e => e.Date >= monthStart).Sum(e => e.Amount);

        foreach (var acc in accounts)
        {
            var d = acc.BankAccountDetails;
            if (acc.AccountCategory?.AccountType != AccountType.Bank || d == null || d.InterestRate is not > 0 || d.InterestFrequency == null) continue;
            var months = PeriodMonths(d.InterestFrequency.Value);

            var txs = await _context.Transactions.AsNoTracking()
                .Where(t => t.AccountId == acc.Id)
                .Select(t => new { t.Date, t.Amount, t.Type, t.Kind })
                .ToListAsync();
            if (txs.Count == 0) continue;

            var lastPosted = txs.Where(t => t.Kind == TransactionKind.InterestCredit).Select(t => t.Date.ToLocal().Date).DefaultIfEmpty(DateTime.MinValue).Max();
            var firstTx = txs.Min(t => t.Date.ToLocal().Date);
            var start = lastPosted > DateTime.MinValue ? lastPosted.AddDays(1) : firstTx;
            var floor = today.AddDays(-366);
            if (start < floor) start = floor;

            // signed movement per local day, to rebuild the balance as it stood each day
            var byDay = txs.GroupBy(t => t.Date.ToLocal().Date)
                .ToDictionary(g => g.Key, g => g.Sum(t => t.Type == TransactionType.Income ? t.Amount : -t.Amount));
            decimal BalanceAtEndOf(DateTime day) =>
                acc.Balance - byDay.Where(kv => kv.Key > day).Sum(kv => kv.Value);

            var periods = new List<InterestDueDto>();
            var pStart = start;
            while (true)
            {
                var pEnd = PeriodEndOnOrAfter(pStart, months, d.InterestCreditDay);
                if (pEnd >= today) break;                 // only finished periods
                var days = (pEnd - pStart).Days + 1;
                // Walk the days, tracking the running balance forward from the balance at the start of the period.
                var bal = BalanceAtEndOf(pStart.AddDays(-1));
                decimal sum = 0m;
                for (var day = pStart; day <= pEnd; day = day.AddDays(1))
                {
                    if (byDay.TryGetValue(day, out var mv)) bal += mv;
                    if (bal > 0) sum += bal;
                }
                var avg = days > 0 ? sum / days : 0m;
                var est = Math.Round(sum * d.InterestRate.Value / 100m / 365m, 2);
                if (est > 0)
                    periods.Add(new InterestDueDto
                    {
                        AccountId = acc.Id, AccountName = acc.Name,
                        PeriodStart = pStart, PeriodEnd = pEnd, Days = days, AutoPost = d.AutoPostInterest,
                        AverageBalance = Math.Round(avg, 2), RatePercent = d.InterestRate.Value, EstimatedAmount = est
                    });
                pStart = pEnd.AddDays(1);
            }
            result.Due.AddRange(periods.OrderByDescending(p => p.PeriodEnd).Take(MaxInterestPeriodsListed).OrderBy(p => p.PeriodEnd));
        }
        return Result.Success(result);
    }

    public async Task<Result<bool>> PostInterestAsync(string userId, PostInterestDto dto)
    {
        if (dto.Amount <= 0)
            return Result.Failure<bool>(new Error("Interest.InvalidAmount", "Interest amount must be greater than zero."));

        var overview = await GetInterestOverviewAsync(userId);
        if (overview.IsFailure) return Result.Failure<bool>(overview.Error);
        var period = overview.Value.Due.FirstOrDefault(p => p.AccountId == dto.AccountId && p.PeriodEnd.Date == dto.PeriodEnd.Date);
        if (period == null)
            return Result.Failure<bool>(new Error("Interest.NotDue", "That interest period is not due, or it was already recorded."));
        if (dto.Amount > period.EstimatedAmount * 3m + 100m)
            return Result.Failure<bool>(new Error("Interest.TooHigh",
                $"₹{dto.Amount:F2} is far above the ₹{period.EstimatedAmount:F2} expected for this period. Check the amount."));

        var account = await _context.Accounts.FirstOrDefaultAsync(a => a.Id == dto.AccountId && a.UserId == userId);
        if (account == null) return Result.Failure<bool>(new Error("Account.NotFound", "Account not found."));

        var dateUtc = period.PeriodEnd.Date.ToUtc();
        if (await _context.Transactions.AnyAsync(t => t.AccountId == account.Id && t.Kind == TransactionKind.InterestCredit && t.Date == dateUtc))
            return Result.Failure<bool>(new Error("Interest.AlreadyPosted", "Interest for this period is already recorded."));

        _context.Transactions.Add(new Transaction
        {
            UserId = userId,
            AccountId = account.Id,
            Description = $"Interest credited{(dto.IsAuto ? " (estimated)" : "")} ({period.PeriodStart:dd MMM}–{period.PeriodEnd:dd MMM yyyy})",
            Amount = Math.Round(dto.Amount, 2),
            Date = dateUtc,
            Type = TransactionType.Income,
            Kind = TransactionKind.InterestCredit
        });
        account.Balance += Math.Round(dto.Amount, 2);
        _context.Accounts.Update(account);

        var save = await ConcurrencySafeSave.TrySaveChangesAsync(_context);
        if (save.IsFailure) return Result.Failure<bool>(save.Error);
        await _cache.InvalidateDashboardAsync(userId);
        return Result.Success(true);
    }
}
