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
/// KEY FIX over original:
///   Original re-threw inside the foreach, which aborted the entire loop the moment
///   one transaction failed — meaning transactions #3..#10 never ran if #2 failed.
///   
///   New behaviour: iterate ALL due transactions, collect failures, then throw
///   an AggregateException at the end if any failed. This gives Hangfire the
///   failed signal it needs for retry, while ensuring every other transaction
///   that CAN succeed does succeed.
///
///   SaveChangesAsync is called per-transaction (not once at the end) so
///   successful ones are persisted immediately and not rolled back by a later failure.
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

        // ── Collect failures; don't abort the loop on first error ─────────────
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
                    rt.NextProcessDate   = CalculateNextDate(rt.NextProcessDate, rt.Frequency);

                    // Deactivate if end date has passed
                    if (rt.EndDate.HasValue && rt.NextProcessDate > rt.EndDate.Value)
                    {
                        rt.IsActive = false;
                        _logger.LogInformation(
                            "Recurring transaction {Id} deactivated — end date {EndDate} reached.",
                            rt.Id, rt.EndDate);
                    }

                    _context.RecurringTransactions.Update(rt);

                    // Save per-transaction so successes are persisted independently
                    await _context.SaveChangesAsync(default);

                    _logger.LogInformation(
                        "Recurring transaction {Id} processed. Next run: {NextDate}",
                        rt.Id, rt.NextProcessDate);
                }
                else
                {
                    // Application-level failure (e.g. account deleted, insufficient funds)
                    var ex = new InvalidOperationException(
                        $"Application error for recurring tx {rt.Id}: {result.Error.Description}");
                    failures.Add((rt.Id, rt.Description ?? "", ex));
                    _logger.LogError(ex, "Failed to process recurring transaction {Id}", rt.Id);
                }
            }
            catch (Exception ex)
            {
                // Infrastructure-level failure (DB timeout, etc.)
                failures.Add((rt.Id, rt.Description ?? "", ex));
                _logger.LogError(ex, "Exception processing recurring transaction {Id}", rt.Id);
                // Continue to next — do NOT re-throw here
            }
        }

        // ── If any failed, throw so Hangfire marks this execution as failed ────
        // Hangfire will apply its configured retry policy (exponential backoff).
        // The next run will only attempt the STILL-due transactions (since
        // successful ones already had their NextProcessDate advanced).
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

    private static DateTime CalculateNextDate(DateTime current, RecurrenceFrequency frequency)
        => frequency switch
        {
            RecurrenceFrequency.Daily   => current.AddDays(1),
            RecurrenceFrequency.Weekly  => current.AddDays(7),
            RecurrenceFrequency.Monthly => current.AddMonths(1),
            RecurrenceFrequency.Yearly  => current.AddYears(1),
            _ => throw new ArgumentOutOfRangeException(nameof(frequency), frequency, null)
        };
}
