using Application.Common.Helpers;
using Application.Common.Models;
using Application.Contracts;
using Application.DTOs.Budgets;
using Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Application.Features.Budgets.Queries;

public record GetBudgetDashboardQuery(string UserId, DateTime RequestDate) : IRequest<Result<BudgetDashboardDto>>;

/// <summary>
/// Everything the /budgets dashboard needs in one call: live progress per budget, headline totals for the
/// user's current money month, a six-month spent-vs-budgeted history, and recent alert events.
/// </summary>
public class GetBudgetDashboardQueryHandler : IRequestHandler<GetBudgetDashboardQuery, Result<BudgetDashboardDto>>
{
    private const int HistoryMonths = 6;

    private readonly IApplicationDbContext _context;
    private readonly IUserSettingsService _settings;
    private readonly ISender _sender;

    public GetBudgetDashboardQueryHandler(IApplicationDbContext context, IUserSettingsService settings, ISender sender)
    {
        _context = context;
        _settings = settings;
        _sender = sender;
    }

    public async Task<Result<BudgetDashboardDto>> Handle(GetBudgetDashboardQuery request, CancellationToken ct)
    {
        var progressResult = await _sender.Send(new GetBudgetProgressQuery(request.UserId, request.RequestDate), ct);
        if (progressResult.IsFailure)
            return Result.Failure<BudgetDashboardDto>(progressResult.Error);

        var general = await _settings.GetGeneralAsync(request.UserId, ct);
        var budgetSettings = await _settings.GetBudgetSettingsAsync(request.UserId, ct);
        var progress = progressResult.Value;
        var monthly = progress.Where(p => p.Period == BudgetPeriod.Monthly).ToList();

        var (monthStart, monthEnd) = AppTimeZone.MonthBoundsUtc(request.RequestDate, general.MonthStartDay);
        var todayLocal = request.RequestDate.ToLocal().Date;
        var periodStartLocal = monthStart.ToLocal().Date;
        var periodEndLocal = monthEnd.ToLocal().Date;

        var dto = new BudgetDashboardDto
        {
            Budgets = progress,
            TotalBudgeted = monthly.Sum(p => p.BudgetAmount),
            TotalSpent = monthly.Sum(p => p.SpentAmount),
            SafeCount = progress.Count(p => p.Status == "safe"),
            AlertCount = progress.Count(p => p.Status == "alert"),
            OverCount = progress.Count(p => p.Status is "limit" or "over"),
            PeriodStart = periodStartLocal,
            PeriodEnd = periodEndLocal,
            DaysLeft = Math.Max(1, (periodEndLocal - todayLocal).Days + 1),
            Settings = budgetSettings,
            MonthStartDay = general.MonthStartDay
        };

        // ── History: the last six money-months, oldest first ──────────────────────────────────────────────
        if (monthly.Count > 0)
        {
            var windows = new List<(DateTime Start, DateTime End)> { (monthStart, monthEnd) };
            for (var i = 1; i < HistoryMonths; i++)
            {
                var prev = AppTimeZone.MonthBoundsUtc(windows[^1].Start.AddTicks(-1), general.MonthStartDay);
                windows.Add(prev);
            }
            windows.Reverse();

            var accountIds = await _context.Accounts.AsNoTracking()
                .Where(a => a.UserId == request.UserId && !a.IsDeleted)
                .Select(a => a.Id).ToListAsync(ct);

            // Hoisted: EF expression trees can't contain the from-end index (^1).
            var rangeStart = windows[0].Start;
            var rangeEnd = windows[windows.Count - 1].End;
            var rows = await _context.Transactions.AsNoTracking()
                .Where(t => accountIds.Contains(t.AccountId) && !t.IsDeleted
                         && t.Type == TransactionType.Expense && t.TransferGroupId == null
                         && t.Date >= rangeStart && t.Date <= rangeEnd)
                .Select(t => new { t.TransactionCategoryId, t.Date, t.Amount })
                .ToListAsync(ct);

            var rollupMap = await CategoryRollup.BuildRollupMapAsync(_context, request.UserId, ct);

            foreach (var (start, end) in windows)
            {
                var inWindow = rows.Where(r => r.Date >= start && r.Date <= end).ToList();
                decimal spent = 0, budgeted = 0;
                foreach (var b in monthly)
                {
                    // Budget amounts aren't versioned, so each month is compared against the budgets that existed in it.
                    var startLocal = start.ToLocal().Date;
                    var endLocal = end.ToLocal().Date;
                    if (b.StartDate.ToLocal().Date > endLocal || (b.EndDate.HasValue && b.EndDate.Value.ToLocal().Date < startLocal))
                        continue;
                    budgeted += b.BudgetAmount;
                    spent += inWindow
                        .Where(r => b.TransactionCategoryId == null
                                    || CategoryRollup.Matches(rollupMap, b.TransactionCategoryId, r.TransactionCategoryId))
                        .Sum(r => Math.Abs(r.Amount));
                }
                dto.History.Add(new BudgetHistoryPointDto
                {
                    Label = start.ToLocal().ToString("MMM"),
                    Spent = spent,
                    Budgeted = budgeted
                });
            }
        }

        // ── Alerts (behaviour log) ──────────────────────────────────────────────────────────────────────
        dto.RecentAlerts = await _context.BudgetAlertEvents.AsNoTracking()
            .Where(a => a.UserId == request.UserId)
            .OrderByDescending(a => a.CreatedAt)
            .Take(8)
            .Select(a => new BudgetAlertEventDto
            {
                BudgetId = a.BudgetId,
                CategoryName = a.CategoryName,
                Level = a.Level,
                ThresholdPercent = a.ThresholdPercent,
                PercentUsed = a.PercentUsed,
                SpentAmount = a.SpentAmount,
                BudgetAmount = a.BudgetAmount,
                CreatedAt = a.CreatedAt
            })
            .ToListAsync(ct);

        dto.AlertsThisPeriod = await _context.BudgetAlertEvents.AsNoTracking()
            .CountAsync(a => a.UserId == request.UserId && a.CreatedAt >= monthStart && a.CreatedAt <= monthEnd, ct);

        return Result<BudgetDashboardDto>.Success(dto);
    }
}
