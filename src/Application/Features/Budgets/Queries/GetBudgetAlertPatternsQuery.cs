using System.Globalization;
using Application.Common.Helpers;
using Application.Common.Models;
using Application.Contracts;
using Application.DTOs.Budgets;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Application.Features.Budgets.Queries;

public record GetBudgetAlertPatternsQuery(string UserId, int Months) : IRequest<Result<BudgetAlertPatternsDto>>;

public class GetBudgetAlertPatternsQueryHandler : IRequestHandler<GetBudgetAlertPatternsQuery, Result<BudgetAlertPatternsDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly IUserSettingsService _settings;

    public GetBudgetAlertPatternsQueryHandler(IApplicationDbContext context, IUserSettingsService settings)
    {
        _context = context;
        _settings = settings;
    }

    public async Task<Result<BudgetAlertPatternsDto>> Handle(GetBudgetAlertPatternsQuery request, CancellationToken ct)
    {
        var months = Math.Clamp(request.Months, 1, 24);
        var startDay = (await _settings.GetGeneralAsync(request.UserId, ct)).MonthStartDay;
        var now = DateTime.UtcNow;
        var (curStart, _) = AppTimeZone.MonthBoundsUtc(now, startDay);
        var since = curStart.ToLocal().AddMonths(-(months - 1)).ToUtc();

        var events = await _context.BudgetAlertEvents
            .AsNoTracking()
            .Where(e => e.UserId == request.UserId && e.CreatedAt >= since)
            .Select(e => new { e.CategoryName, e.Level, e.CreatedAt })
            .ToListAsync(ct);

        // Each event placed in its money-month, with the day-of-month it landed on.
        var placed = events.Select(e =>
        {
            var (ms, _) = AppTimeZone.MonthBoundsUtc(e.CreatedAt, startDay);
            var local = e.CreatedAt.ToLocal();
            var msLocal = ms.ToLocal();
            return new
            {
                Category = string.IsNullOrWhiteSpace(e.CategoryName) ? "Overall" : e.CategoryName!,
                e.Level,
                MonthKey = msLocal.Date,
                DayInMonth = Math.Max(1, (local.Date - msLocal.Date).Days + 1),
                Weekday = ((int)local.DayOfWeek + 6) % 7
            };
        }).ToList();

        var dto = new BudgetAlertPatternsDto
        {
            Months = months,
            TotalAlerts = placed.Count,
            Warnings = placed.Count(p => p.Level < 2),
            Breaches = placed.Count(p => p.Level >= 2)
        };

        foreach (var p in placed)
        {
            dto.ByWeekday[p.Weekday]++;
            dto.ByPartOfMonth[p.DayInMonth <= 10 ? 0 : p.DayInMonth <= 20 ? 1 : 2]++;
        }

        // Month series, oldest first, including empty months so gaps are visible.
        var firstMonth = since.ToLocal().Date;
        for (var i = 0; i < months; i++)
        {
            var key = firstMonth.AddMonths(i);
            // Align to the money-month that contains this start.
            var (ms, _) = AppTimeZone.MonthBoundsUtc(key.ToUtc().AddHours(12), startDay);
            var msLocal = ms.ToLocal().Date;
            dto.ByMonth.Add(new BudgetAlertMonthPointDto
            {
                Label = msLocal.ToString("MMM yy", CultureInfo.InvariantCulture),
                Warnings = placed.Count(p => p.MonthKey == msLocal && p.Level < 2),
                Breaches = placed.Count(p => p.MonthKey == msLocal && p.Level >= 2)
            });
        }

        dto.Categories = placed.GroupBy(p => p.Category).Select(g =>
        {
            var breaches = g.Where(x => x.Level >= 2).ToList();
            var firstPerMonth = breaches.GroupBy(x => x.MonthKey).Select(m => m.Min(x => x.DayInMonth)).ToList();
            return new BudgetAlertCategoryPatternDto
            {
                CategoryName = g.Key,
                Warnings = g.Count(x => x.Level < 2),
                Breaches = breaches.Count,
                MonthsBreached = firstPerMonth.Count,
                AvgBreachDay = firstPerMonth.Count == 0 ? 0 : Math.Round(firstPerMonth.Average(), 0)
            };
        }).OrderByDescending(c => c.MonthsBreached).ThenByDescending(c => c.Warnings + c.Breaches).ToList();

        dto.Insights = BuildInsights(dto, months);
        return Result.Success(dto);
    }

    private static List<string> BuildInsights(BudgetAlertPatternsDto d, int months)
    {
        var list = new List<string>();
        if (d.TotalAlerts == 0)
        {
            list.Add($"No budget alerts in the last {months} months. Either you are well within your budgets or no alerts have been logged yet.");
            return list;
        }

        var repeat = d.Categories.Where(c => c.MonthsBreached >= 2).Take(2).ToList();
        foreach (var c in repeat)
            list.Add($"{c.CategoryName} hit its limit in {c.MonthsBreached} of the last {months} months" +
                     (c.AvgBreachDay > 0 ? $", usually around day {c.AvgBreachDay:0} of your month." : "."));

        var days = new[] { "Mondays", "Tuesdays", "Wednesdays", "Thursdays", "Fridays", "Saturdays", "Sundays" };
        var maxDay = Array.IndexOf(d.ByWeekday, d.ByWeekday.Max());
        if (d.TotalAlerts >= 5 && d.ByWeekday[maxDay] * 100 / d.TotalAlerts >= 30)
            list.Add($"{d.ByWeekday[maxDay] * 100 / d.TotalAlerts}% of alerts are triggered on {days[maxDay]}.");

        var parts = new[] { "the first 10 days", "days 11–20", "the last stretch (day 21 onwards)" };
        var maxPart = Array.IndexOf(d.ByPartOfMonth, d.ByPartOfMonth.Max());
        if (d.TotalAlerts >= 5 && d.ByPartOfMonth[maxPart] * 100 / d.TotalAlerts >= 50)
            list.Add($"Most alerts ({d.ByPartOfMonth[maxPart] * 100 / d.TotalAlerts}%) come in {parts[maxPart]} of the month.");

        var half = d.ByMonth.Count / 2;
        if (half >= 1)
        {
            var older = d.ByMonth.Take(half).Sum(m => m.Warnings + m.Breaches);
            var recent = d.ByMonth.Skip(half).Sum(m => m.Warnings + m.Breaches);
            if (recent > older && recent >= 3) list.Add("Alerts are becoming more frequent compared with earlier months.");
            else if (older > recent && older >= 3) list.Add("Alerts are becoming less frequent — spending is getting more controlled.");
        }
        if (list.Count == 0) list.Add("No strong pattern yet — keep logging and this will sharpen.");
        return list;
    }
}
