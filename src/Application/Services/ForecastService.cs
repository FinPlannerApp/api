using System.Globalization;
using Application.Common.Helpers;
using Application.Common.Models;
using Application.Contracts;
using Application.DTOs.Forecast;
using Domain.Entities;
using Domain.Enums;
using Microsoft.EntityFrameworkCore;
using AccountType = Domain.Enums.AccountType;

namespace Application.Services;

public interface IForecastService
{
    Task<Result<ForecastDto>> GetForecastAsync(string userId);
    Task<Result<CardsOverviewDto>> GetCardsAsync(string userId);
    Task<Result<AccountsDashboardDto>> GetAccountsDashboardAsync(string userId);
}

/// <summary>
/// Patterns and projections from the user's own history. Deterministic arithmetic (medians, quartiles, schedules),
/// no machine learning. Everything here is an estimate and is labelled as one by the UI.
/// </summary>
public class ForecastService : IForecastService
{
    private readonly IApplicationDbContext _context;
    private readonly IAccountService _accounts;
    public ForecastService(IApplicationDbContext context, IAccountService accounts) { _context = context; _accounts = accounts; }

    private sealed record Tx(int AccountId, DateTime Day, decimal Amount, TransactionType Type, TransactionKind Kind,
        bool IsTransfer, int? CategoryId, int? RecurringId, string Description);

    private static bool IsSpend(Tx t) => t.Type == TransactionType.Expense && !t.IsTransfer && t.Kind is TransactionKind.Normal or TransactionKind.LoanInterest;
    private static bool IsIncome(Tx t) => t.Type == TransactionType.Income && !t.IsTransfer && t.Kind is TransactionKind.Normal or TransactionKind.InterestCredit;
    private static string Key(DateTime d) => $"{d.Year:D4}-{d.Month:D2}";
    private static decimal Signed(Tx t) => t.Type == TransactionType.Income ? t.Amount : -t.Amount;

    private static decimal Percentile(IReadOnlyList<decimal> values, double p)
    {
        if (values.Count == 0) return 0m;
        var s = values.OrderBy(v => v).ToList();
        var pos = (s.Count - 1) * p; var lo = (int)Math.Floor(pos); var hi = (int)Math.Ceiling(pos);
        return Math.Round(s[lo] + (s[hi] - s[lo]) * (decimal)(pos - lo), 2);
    }
    private static decimal Median(IReadOnlyList<decimal> v) => Percentile(v, 0.5);

    private async Task<List<Tx>> LoadAsync(string userId, DateTime windowStartLocal)
    {
        var startUtc = windowStartLocal.ToUtc();
        var rows = await _context.Transactions.AsNoTracking()
            .Where(t => t.UserId == userId && t.Date >= startUtc)
            .Select(t => new { t.AccountId, t.Date, t.Amount, t.Type, t.Kind, t.TransferGroupId, t.TransactionCategoryId, t.RecurringTransactionId, t.Description, t.IsBalanceAdjustment, t.IsOpeningBalance })
            .ToListAsync();
        return rows.Select(t => new Tx(t.AccountId, t.Date.ToLocal().Date, t.Amount, t.Type, t.Kind,
            t.TransferGroupId != null || t.IsBalanceAdjustment || t.IsOpeningBalance, t.TransactionCategoryId, t.RecurringTransactionId, t.Description ?? "")).ToList();
    }

    // ───────────── recurring schedule expansion ─────────────
    private static IEnumerable<DateTime> Occurrences(RecurringTransaction rt, DateTime fromUtc, DateTime toUtc)
    {
        var d = rt.NextProcessDate;
        for (var n = 0; n < 500 && d < toUtc; n++)
        {
            if (rt.EndDate.HasValue && d > rt.EndDate.Value) yield break;
            if (d >= fromUtc) yield return d;
            DateTime next;
            switch (rt.Frequency)
            {
                case RecurrenceFrequency.Daily: next = d.AddDays(1); break;
                case RecurrenceFrequency.Weekly: next = d.AddDays(7); break;
                case RecurrenceFrequency.Monthly: next = RecurrenceCalculator.NextEveryMonthsAfter(rt.StartDate, d, 1); break;
                case RecurrenceFrequency.Quarterly: next = RecurrenceCalculator.NextEveryMonthsAfter(rt.StartDate, d, 3); break;
                case RecurrenceFrequency.HalfYearly: next = RecurrenceCalculator.NextEveryMonthsAfter(rt.StartDate, d, 6); break;
                case RecurrenceFrequency.Yearly: next = RecurrenceCalculator.NextEveryMonthsAfter(rt.StartDate, d, 12); break;
                case RecurrenceFrequency.Custom:
                    if (rt.CustomDays is null or RecurrenceDayOfWeek.None) yield break;
                    next = d.AddDays(1);
                    for (var i = 0; i < 7 && !HasDay(rt.CustomDays.Value, next.ToLocal().DayOfWeek); i++) next = next.AddDays(1);
                    break;
                default: yield break; // one-time
            }
            d = next;
        }
    }

    private static bool HasDay(RecurrenceDayOfWeek set, DayOfWeek d) => d switch
    {
        DayOfWeek.Monday => set.HasFlag(RecurrenceDayOfWeek.Monday), DayOfWeek.Tuesday => set.HasFlag(RecurrenceDayOfWeek.Tuesday),
        DayOfWeek.Wednesday => set.HasFlag(RecurrenceDayOfWeek.Wednesday), DayOfWeek.Thursday => set.HasFlag(RecurrenceDayOfWeek.Thursday),
        DayOfWeek.Friday => set.HasFlag(RecurrenceDayOfWeek.Friday), DayOfWeek.Saturday => set.HasFlag(RecurrenceDayOfWeek.Saturday),
        _ => set.HasFlag(RecurrenceDayOfWeek.Sunday),
    };

    // ───────────── forecast ─────────────
    public async Task<Result<ForecastDto>> GetForecastAsync(string userId)
    {
        var today = AppTimeZone.TodayLocal();
        var thisMonth = new DateTime(today.Year, today.Month, 1);
        var txs = await LoadAsync(userId, thisMonth.AddMonths(-6));
        var cats = await _context.TransactionCategories.AsNoTracking().Where(c => c.UserId == userId).ToDictionaryAsync(c => c.Id, c => c.Name);
        string CatName(int? id) => id.HasValue && cats.TryGetValue(id.Value, out var n) ? n : "Uncategorised";

        var dto = new ForecastDto();
        var keys = Enumerable.Range(1, 6).Reverse().Select(i => thisMonth.AddMonths(-i)).ToList();   // last 6 complete months, oldest first

        foreach (var m in keys)
        {
            var k = Key(m);
            var income = txs.Where(t => Key(t.Day) == k && IsIncome(t)).Sum(t => t.Amount);
            var spend = txs.Where(t => Key(t.Day) == k && IsSpend(t)).Sum(t => t.Amount);
            dto.History.Add(new MonthPointDto { Month = k, Income = income, Spending = spend, SavingsRatePercent = income > 0 ? Math.Round((income - spend) / income * 100m, 1) : null });
        }

        // variable = everyday spending that no schedule produced
        var varByMonthCat = txs.Where(t => IsSpend(t) && t.RecurringId == null && t.Day < thisMonth)
            .GroupBy(t => (Key(t.Day), t.CategoryId)).ToDictionary(g => g.Key, g => g.Sum(t => t.Amount));
        var varTotals = keys.Select(m => varByMonthCat.Where(kv => kv.Key.Item1 == Key(m)).Sum(kv => kv.Value)).ToList();
        var withData = varTotals.Where(v => v > 0).ToList();
        var monthsOfData = dto.History.Count(h => h.Spending > 0);

        var next = new NextMonthDto
        {
            Label = thisMonth.AddMonths(1).ToString("MMMM yyyy", CultureInfo.InvariantCulture),
            MonthsOfData = monthsOfData, Confidence = monthsOfData >= 3 ? "ok" : "low",
            VariableTypical = Median(withData), VariableLow = Percentile(withData, 0.25), VariableHigh = Percentile(withData, 0.75),
        };
        foreach (var catId in varByMonthCat.Keys.Select(k => k.CategoryId).Distinct())
        {
            var perMonth = keys.Select(m => varByMonthCat.TryGetValue((Key(m), catId), out var v) ? v : 0m).ToList();
            var typical = Median(perMonth);
            if (typical > 0) next.TopCategories.Add(new CategoryTypicalDto { Name = CatName(catId), Typical = typical });
        }
        next.TopCategories = next.TopCategories.OrderByDescending(c => c.Typical).Take(6).ToList();

        var nextStart = thisMonth.AddMonths(1).ToUtc(); var nextEnd = thisMonth.AddMonths(2).ToUtc();
        var rts = await _context.RecurringTransactions.AsNoTracking()
            .Where(r => r.UserId == userId && r.IsActive && r.PausedAt == null).ToListAsync();
        foreach (var r in rts)
        {
            var n = Occurrences(r, nextStart, nextEnd).Count();
            if (n == 0) continue;
            var total = n * r.Amount;
            if (r.Type == TransactionType.Income) next.ExpectedIncome += total;
            else if (r.LinkedInvestmentAccountId.HasValue) next.Investing += total;
            else next.FixedBills += total;
        }
        if (next.ExpectedIncome == 0m)
        {
            var inc = dto.History.Where(h => h.Income > 0).Select(h => h.Income).ToList();
            next.ExpectedIncome = Median(inc); next.IncomeFromHistory = next.ExpectedIncome > 0;
        }
        var committed = next.FixedBills + next.Investing;
        next.LeftTypical = next.ExpectedIncome - committed - next.VariableTypical;
        next.LeftLow = next.ExpectedIncome - committed - next.VariableHigh;
        next.LeftHigh = next.ExpectedIncome - committed - next.VariableLow;
        dto.NextMonth = next;

        // ── accounts used by runway + balance progression ──
        var accRes = await _accounts.GetAllAccountsAsync(userId);
        var liquid = accRes.IsSuccess
            ? accRes.Value.Where(a => !a.IsArchived && !a.IsLiability && a.CashRole != CashRole.HeldForOthers
                && a.AccountType is AccountType.Bank or AccountType.Cash or AccountType.Other).ToList()
            : new List<DTOs.Accounts.AccountDto>();
        var liquidIds = liquid.Select(a => a.Id).ToHashSet();

        if (liquid.Count > 0)
        {
            var start = liquid.Sum(a => a.Balance);
            // balance at the end of each completed month = now minus everything that moved afterwards
            foreach (var m in keys)
            {
                var end = m.AddMonths(1).AddDays(-1);
                var after = txs.Where(t => liquidIds.Contains(t.AccountId) && t.Day > end).Sum(Signed);
                dto.LiquidBalance.Add(new BalancePointDto { Month = Key(m), Balance = Math.Round(start - after, 2) });
            }
            dto.LiquidBalance.Add(new BalancePointDto { Month = "now", Balance = start });

            const int horizon = 45;
            var daily = next.VariableTypical / 30m;
            var events = new Dictionary<DateTime, decimal>();
            void Add(DateTime d, decimal v) => events[d] = events.GetValueOrDefault(d) + v;
            var fromUtc = today.ToUtc(); var toUtc = today.AddDays(horizon).ToUtc();
            foreach (var r in rts.Where(r => liquidIds.Contains(r.AccountId)))
                foreach (var o in Occurrences(r, fromUtc, toUtc))
                    Add(o.ToLocal().Date, r.Type == TransactionType.Income ? r.Amount : -r.Amount);
            foreach (var c in accRes.Value.Where(a => !a.IsArchived && a.AccountType == AccountType.CreditCard))
            {
                var cc = c.CreditCardDetails;
                if (cc?.RemainingBillAmount is > 0 && cc.DueDate.HasValue && cc.DueDate.Value.Date >= today && cc.DueDate.Value.Date < today.AddDays(horizon))
                    Add(cc.DueDate.Value.Date, -cc.RemainingBillAmount.Value);
            }

            var bal = start; var lowest = start; var lowestOn = today; DateTime? shortOn = null;
            for (var i = 0; i < horizon; i++)
            {
                var d = today.AddDays(i);
                bal -= daily;
                if (events.TryGetValue(d, out var e)) bal += e;
                if (bal < lowest) { lowest = bal; lowestOn = d; }
                if (bal < 0 && shortOn == null) shortOn = d;
            }
            dto.Runway = new RunwayDto { StartBalance = start, LowestBalance = Math.Round(lowest, 2), LowestOn = lowestOn, ShortOn = shortOn, HorizonDays = horizon, DailyVariable = Math.Round(daily, 2) };
        }

        BuildInsights(dto, txs, today, thisMonth, keys, next, CatName);
        return Result.Success(dto);
    }

    private static void BuildInsights(ForecastDto dto, List<Tx> txs, DateTime today, DateTime thisMonth, List<DateTime> keys,
        NextMonthDto next, Func<int?, string> catName)
    {
        // 1) pace this month
        var mtd = txs.Where(t => IsSpend(t) && t.RecurringId == null && t.Day >= thisMonth).Sum(t => t.Amount);
        var daysIn = DateTime.DaysInMonth(today.Year, today.Month);
        if (today.Day >= 7 && next.VariableTypical > 0)
        {
            var projected = mtd / today.Day * daysIn;
            var ratio = projected / next.VariableTypical;
            if (ratio >= 1.15m)
                dto.Insights.Add(new InsightDto { Kind = "pace", Severity = "watch", Title = "Everyday spending is running high this month",
                    Detail = $"At this pace you'll spend about ₹{projected:N0} on everyday things, roughly {Math.Round((ratio - 1) * 100m):N0}% above your typical ₹{next.VariableTypical:N0}." });
            else if (ratio <= 0.85m)
                dto.Insights.Add(new InsightDto { Kind = "pace", Title = "Everyday spending is lower than usual",
                    Detail = $"At this pace about ₹{projected:N0} against a typical ₹{next.VariableTypical:N0}." });
        }

        // 2) a category that jumped last month
        var lastKey = Key(keys[^1]); var prior = keys.Take(keys.Count - 1).Skip(Math.Max(0, keys.Count - 4)).Select(Key).ToList();
        var byCat = txs.Where(t => IsSpend(t) && t.RecurringId == null && t.Day < thisMonth)
            .GroupBy(t => t.CategoryId).Select(g => new
            {
                Id = g.Key,
                Last = g.Where(t => Key(t.Day) == lastKey).Sum(t => t.Amount),
                Prior = prior.Select(k => g.Where(t => Key(t.Day) == k).Sum(t => t.Amount)).ToList(),
            });
        foreach (var c in byCat.OrderByDescending(c => c.Last).Take(12))
        {
            var seen = c.Prior.Where(p => p > 0).ToList();
            if (seen.Count < 2) continue;
            var avg = seen.Average();
            if (c.Last >= avg * 1.3m && c.Last - avg >= 500m)
                dto.Insights.Add(new InsightDto { Kind = "category", Severity = "watch", Title = $"{catName(c.Id)} went up last month",
                    Detail = $"₹{c.Last:N0} against about ₹{avg:N0} in the months before." });
            if (dto.Insights.Count(i => i.Kind == "category") >= 3) break;
        }

        // 3) unusually large recent purchases (needs enough history in that category to mean anything)
        var cutoff = today.AddDays(-30);
        foreach (var g in txs.Where(t => IsSpend(t) && t.RecurringId == null).GroupBy(t => t.CategoryId))
        {
            var old = g.Where(t => t.Day < cutoff).Select(t => t.Amount).ToList();
            if (old.Count < 8) continue;
            var mean = old.Average(); var sd = (decimal)Math.Sqrt(old.Sum(v => Math.Pow((double)(v - mean), 2)) / old.Count);
            foreach (var t in g.Where(t => t.Day >= cutoff && t.Amount >= 1000m && t.Amount > mean + 3 * sd).OrderByDescending(t => t.Amount).Take(1))
                dto.Insights.Add(new InsightDto { Kind = "anomaly", Severity = "watch", Title = $"Unusually large {catName(g.Key)} spend",
                    Detail = $"₹{t.Amount:N0} on {t.Day:dd MMM} ({(string.IsNullOrWhiteSpace(t.Description) ? "no description" : t.Description)}). Your usual is about ₹{mean:N0}." });
            if (dto.Insights.Count(i => i.Kind == "anomaly") >= 3) break;
        }

        // 4) savings rate trend
        var rates = dto.History.Where(h => h.SavingsRatePercent.HasValue).Select(h => h.SavingsRatePercent!.Value).ToList();
        if (rates.Count >= 6)
        {
            var recent = rates.Skip(3).Average(); var before = rates.Take(3).Average();
            if (recent - before <= -10m)
                dto.Insights.Add(new InsightDto { Kind = "savings", Severity = "watch", Title = "Your savings rate has dropped",
                    Detail = $"About {Math.Round(recent):N0}% over the last 3 months, down from {Math.Round(before):N0}% before that." });
            else if (recent - before >= 10m)
                dto.Insights.Add(new InsightDto { Kind = "savings", Title = "Your savings rate is improving",
                    Detail = $"About {Math.Round(recent):N0}% over the last 3 months, up from {Math.Round(before):N0}%." });
        }
    }

    // ───────────── cards ─────────────
    public async Task<Result<CardsOverviewDto>> GetCardsAsync(string userId)
    {
        var today = AppTimeZone.TodayLocal();
        var thisMonth = new DateTime(today.Year, today.Month, 1);
        var accRes = await _accounts.GetAllAccountsAsync(userId);
        if (accRes.IsFailure) return Result.Failure<CardsOverviewDto>(accRes.Error);
        var cards = accRes.Value.Where(a => !a.IsArchived && a.AccountType == AccountType.CreditCard).ToList();
        var o = new CardsOverviewDto();
        if (cards.Count == 0) return Result.Success(o);

        var txs = await LoadAsync(userId, thisMonth.AddMonths(-5));
        var cats = await _context.TransactionCategories.AsNoTracking().Where(c => c.UserId == userId).ToDictionaryAsync(c => c.Id, c => c.Name);
        var keys = Enumerable.Range(0, 6).Reverse().Select(i => thisMonth.AddMonths(-i)).ToList();   // includes the current month
        var since = today.AddDays(-90);

        foreach (var c in cards)
        {
            var mine = txs.Where(t => t.AccountId == c.Id && IsSpend(t)).ToList();
            var cc = c.CreditCardDetails;
            var outstanding = c.Balance < 0 ? -c.Balance : 0m;
            var months = keys.Select(m => new CardSpendMonthDto { Month = Key(m), Spend = mine.Where(t => Key(t.Day) == Key(m)).Sum(t => t.Amount) }).ToList();
            var complete = months.Take(5).Where(m => m.Spend > 0).Select(m => m.Spend).ToList();
            var recent = mine.Where(t => t.Day >= since).ToList();
            var weekday = new decimal[7];
            foreach (var t in recent) weekday[((int)t.Day.DayOfWeek + 6) % 7] += t.Amount;
            var big = recent.OrderByDescending(t => t.Amount).FirstOrDefault();

            o.Cards.Add(new CardDto
            {
                AccountId = c.Id, Name = c.Name, Limit = cc?.CreditLimit, Outstanding = outstanding,
                UtilisationPercent = cc?.CreditLimit is > 0 ? Math.Round(outstanding / cc.CreditLimit.Value * 100m, 1) : null,
                RemainingBill = cc?.RemainingBillAmount, DueDate = cc?.DueDate, InterestRate = cc?.InterestRate,
                AvgMonthlySpend = complete.Count > 0 ? Math.Round(complete.Average(), 2) : months[^1].Spend,
                Purchases90d = recent.Count, Months = months,
                TopCategories = recent.GroupBy(t => t.CategoryId).Select(g => new NameAmountDto
                    { Name = g.Key.HasValue && cats.TryGetValue(g.Key.Value, out var n) ? n : "Uncategorised", Amount = g.Sum(t => t.Amount) })
                    .OrderByDescending(x => x.Amount).Take(6).ToList(),
                Weekday = weekday.ToList(),
                Biggest = big == null ? null : new NameAmountDto { Name = $"{(string.IsNullOrWhiteSpace(big.Description) ? "Purchase" : big.Description)} · {big.Day:dd MMM}", Amount = big.Amount },
            });
        }
        o.TotalLimit = o.Cards.Where(c => c.Limit is > 0).Sum(c => c.Limit!.Value);
        o.TotalOutstanding = o.Cards.Sum(c => c.Outstanding);
        var limited = o.Cards.Where(c => c.Limit is > 0).ToList();
        o.UtilisationPercent = limited.Count > 0 ? Math.Round(limited.Sum(c => c.Outstanding) / limited.Sum(c => c.Limit!.Value) * 100m, 1) : null;
        return Result.Success(o);
    }

    // ───────────── accounts dashboard ─────────────
    public async Task<Result<AccountsDashboardDto>> GetAccountsDashboardAsync(string userId)
    {
        var today = AppTimeZone.TodayLocal();
        var thisMonth = new DateTime(today.Year, today.Month, 1);
        var accRes = await _accounts.GetAllAccountsAsync(userId);
        if (accRes.IsFailure) return Result.Failure<AccountsDashboardDto>(accRes.Error);
        var list = accRes.Value.Where(a => !a.IsArchived && !a.IsLiability && a.AccountType is AccountType.Bank or AccountType.Cash or AccountType.Other).ToList();
        var o = new AccountsDashboardDto();
        if (list.Count == 0) return Result.Success(o);

        var txs = await LoadAsync(userId, thisMonth.AddMonths(-11));
        var keys = Enumerable.Range(0, 6).Reverse().Select(i => thisMonth.AddMonths(-i)).ToList();
        var yearAgo = today.AddDays(-365);

        foreach (var a in list)
        {
            var mine = txs.Where(t => t.AccountId == a.Id).ToList();
            var byDay = mine.GroupBy(t => t.Day).ToDictionary(g => g.Key, g => g.Sum(Signed));
            decimal lowest = a.Balance; DateTime? lowestOn = null;
            for (var i = 0; i <= 90; i++)
            {
                var day = today.AddDays(-i);
                var after = byDay.Where(kv => kv.Key > day).Sum(kv => kv.Value);
                var bal = a.Balance - after;
                if (i == 0 || bal < lowest) { lowest = bal; lowestOn = day; }
            }
            o.Accounts.Add(new AccountDashItemDto
            {
                AccountId = a.Id, Name = a.Name, Balance = a.Balance, LowestIn90Days = Math.Round(lowest, 2), LowestOn = lowestOn,
                InterestLast12Months = mine.Where(t => t.Kind == TransactionKind.InterestCredit && t.Day >= yearAgo).Sum(t => t.Amount),
                Months = keys.Select(m => new AccountFlowMonthDto
                {
                    Month = Key(m),
                    In = mine.Where(t => Key(t.Day) == Key(m) && t.Type == TransactionType.Income && !t.IsTransfer).Sum(t => t.Amount),
                    Out = mine.Where(t => Key(t.Day) == Key(m) && t.Type == TransactionType.Expense && !t.IsTransfer).Sum(t => t.Amount),
                }).ToList(),
            });
        }
        o.TotalBalance = list.Sum(a => a.Balance);
        return Result.Success(o);
    }
}
