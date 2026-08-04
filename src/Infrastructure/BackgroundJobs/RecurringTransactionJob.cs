using Application.Common.Helpers;
using Application.Contracts;
using Application.DTOs.Transactions;
using Domain.Entities;
using Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Infrastructure.BackgroundJobs;

/// <summary>
/// Hangfire job: processes all recurring transactions that are due.
///
/// KEY FIX (earlier): continues past individual failures instead of
/// aborting the whole loop, collects them, throws AggregateException at
/// the end so Hangfire still gets a retry signal.
///
/// NEW: Custom frequency support (e.g. every Mon/Wed/Fri), timezone-aware
/// via AppTimeZone — the day-of-week walk happens in local time, same
/// reasoning as the month-boundary fixes elsewhere: a UTC-stored timestamp
/// near local midnight can read as the wrong day-of-week in UTC terms.
/// </summary>
public class RecurringTransactionJob
{
    private readonly IApplicationDbContext _context;
    private readonly ITransactionService   _transactionService;
    private readonly ILogger<RecurringTransactionJob> _logger;

    public RecurringTransactionJob(
        IApplicationDbContext context,
        ITransactionService transactionService,
        ILogger<RecurringTransactionJob> logger)
    {
        _context            = context;
        _transactionService = transactionService;
        _logger             = logger;
    }

    public async Task ProcessRecurringTransactionsAsync()
    {
        _logger.LogInformation("Hangfire Job: Processing Recurring Transactions...");

        var now = DateTime.UtcNow;

        var dueTransactions = await _context.RecurringTransactions
            .Where(rt => rt.IsActive && !rt.IsDeleted && rt.NextProcessDate <= now)
            .ToListAsync();

        if (dueTransactions.Count == 0)
        {
            _logger.LogInformation("No recurring transactions are due.");
            return;
        }

        _logger.LogInformation("Found {Count} due recurring transactions.", dueTransactions.Count);

        var failures = new List<(int Id, string Desc, Exception Ex)>();

        foreach (var rt in dueTransactions)
        {
            try
            {
                var upsertDto = new UpsertTransactionDto
                {
                    Description           = rt.Description,
                    Amount                = rt.Amount,
                    Type                  = rt.Type,
                    Date                  = rt.NextProcessDate,
                    TransactionCategoryId = rt.TransactionCategoryId
                };

                var result = await _transactionService.UpsertTransactionAsync(rt.UserId, rt.AccountId, upsertDto);

                if (result.IsSuccess)
                {
                    rt.LastProcessedDate = rt.NextProcessDate;
                    rt.NextProcessDate   = CalculateNextDate(rt.NextProcessDate, rt.Frequency, rt.CustomDays);

                    if (rt.EndDate.HasValue && rt.NextProcessDate > rt.EndDate.Value)
                    {
                        rt.IsActive = false;
                        _logger.LogInformation(
                            "Recurring transaction {Id} deactivated — end date {EndDate} reached.",
                            rt.Id, rt.EndDate);
                    }

                    _context.RecurringTransactions.Update(rt);
                    await _context.SaveChangesAsync(default);

                    _logger.LogInformation(
                        "Recurring transaction {Id} processed. Next run: {NextDate}",
                        rt.Id, rt.NextProcessDate);
                }
                else
                {
                    var ex = new InvalidOperationException(
                        $"Application error for recurring tx {rt.Id}: {result.Error.Description}");
                    failures.Add((rt.Id, rt.Description ?? "", ex));
                    _logger.LogError(ex, "Failed to process recurring transaction {Id}", rt.Id);
                }
            }
            catch (Exception ex)
            {
                failures.Add((rt.Id, rt.Description ?? "", ex));
                _logger.LogError(ex, "Exception processing recurring transaction {Id}", rt.Id);
            }
        }

        if (failures.Any())
        {
            var failedIds = string.Join(", ", failures.Select(f => f.Id));
            _logger.LogWarning(
                "Recurring job completed with {FailCount} failure(s). IDs: {Ids}",
                failures.Count, failedIds);

            throw new AggregateException(
                $"{failures.Count} recurring transaction(s) failed: IDs [{failedIds}]",
                failures.Select(f => f.Ex));
        }

        _logger.LogInformation("All recurring transactions processed successfully.");
    }

    private static DateTime CalculateNextDate(DateTime current, RecurrenceFrequency frequency, RecurrenceDayOfWeek? customDays)
        => frequency switch
        {
            RecurrenceFrequency.Daily   => current.AddDays(1),
            RecurrenceFrequency.Weekly  => current.AddDays(7),
            RecurrenceFrequency.Monthly => current.AddMonths(1),
            RecurrenceFrequency.Yearly  => current.AddYears(1),
            RecurrenceFrequency.Custom  => CalculateNextCustomDate(current, customDays),
            _ => throw new ArgumentOutOfRangeException(nameof(frequency), frequency, null)
        };

    /// <summary>
    /// Walks forward from `current` (UTC) day-by-day in LOCAL time until it
    /// finds one whose local day-of-week is in the selected set, then
    /// converts that local date back to UTC for storage — same time-of-day
    /// as the original `current` value, so a recurring transaction that
    /// always fires at local midnight keeps doing so.
    /// </summary>
    private static DateTime CalculateNextCustomDate(DateTime currentUtc, RecurrenceDayOfWeek? customDays)
    {
        if (customDays is null or RecurrenceDayOfWeek.None)
            throw new InvalidOperationException(
                "Custom recurrence requires at least one day selected — this shouldn't happen if the DTO validator ran, but failing loudly here rather than silently picking a default.");

        var currentLocal = currentUtc.ToLocal();

        for (int i = 1; i <= 7; i++)
        {
            var candidateLocal = currentLocal.AddDays(i);
            if ((customDays.Value & DayOfWeekToFlag(candidateLocal.DayOfWeek)) != 0)
                return candidateLocal.ToUtc();
        }

        // Unreachable given the None check above (any non-empty bitmask must
        // match within 7 days), but the compiler doesn't know that.
        throw new InvalidOperationException("Could not determine next custom recurrence date.");
    }

    private static RecurrenceDayOfWeek DayOfWeekToFlag(DayOfWeek day) => day switch
    {
        DayOfWeek.Monday    => RecurrenceDayOfWeek.Monday,
        DayOfWeek.Tuesday   => RecurrenceDayOfWeek.Tuesday,
        DayOfWeek.Wednesday => RecurrenceDayOfWeek.Wednesday,
        DayOfWeek.Thursday  => RecurrenceDayOfWeek.Thursday,
        DayOfWeek.Friday    => RecurrenceDayOfWeek.Friday,
        DayOfWeek.Saturday  => RecurrenceDayOfWeek.Saturday,
        DayOfWeek.Sunday    => RecurrenceDayOfWeek.Sunday,
        _ => throw new ArgumentOutOfRangeException(nameof(day))
    };
}
