using Application.Common.Helpers;
using Application.Contracts;
using Application.DTOs.Accounts;
using Application.DTOs.Transactions;
using Domain.Entities;
using Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Infrastructure.BackgroundJobs;

public class RecurringTransactionJob
{
    private readonly IApplicationDbContext _context;
    private readonly ITransactionService   _transactionService;
    private readonly IAccountService       _accountService;
    private readonly ILogger<RecurringTransactionJob> _logger;

    public RecurringTransactionJob(
        IApplicationDbContext context,
        ITransactionService transactionService,
        IAccountService accountService,
        ILogger<RecurringTransactionJob> logger)
    {
        _context            = context;
        _transactionService = transactionService;
        _accountService     = accountService;
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
          // Catch up every missed occurrence in one run (cap 12). Before, a bill that was
          // 4 months behind took 4 daily runs to clear — and stayed "overdue" meanwhile.
          for (var occurrence = 0; occurrence < 12; occurrence++)
          {
            if (!rt.IsActive || rt.NextProcessDate > now) break;
            var committed = false;

            await using var dbTransaction = await ((DbContext)_context).Database.BeginTransactionAsync();

            try
            {
                // Two API instances (or an overlapping run) must never post the same occurrence twice.
                // Take a transaction-scoped lock on this item; if someone else holds it, skip it.
                if (!await TryLockAsync(rt.Id))
                {
                    await dbTransaction.RollbackAsync();
                    _logger.LogInformation("Recurring transaction {Id} is being processed elsewhere; skipping.", rt.Id);
                    break;
                }

                // The list was loaded before we got the lock. If another run already processed this
                // occurrence and committed, our copy is stale: reload and re-check before posting anything.
                await ((DbContext)_context).Entry(rt).ReloadAsync();
                if (!rt.IsActive || rt.IsDeleted || rt.NextProcessDate > now)
                {
                    await dbTransaction.RollbackAsync();
                    break;
                }

                bool success;

                if (rt.LinkedLoanAccountId.HasValue)
                {
                    // Routes through the proper interest/principal split
                    // instead of a plain expense — the whole point of
                    // wiring this up. Same atomic transaction wraps this
                    // call too, since MakeLoanPaymentAsync's own
                    // SaveChangesAsync participates in the already-open
                    // explicit transaction rather than committing independently.
                    var loanResult = await _accountService.MakeLoanPaymentAsync(rt.UserId, new MakeLoanPaymentDto
                    {
                        LoanAccountId = rt.LinkedLoanAccountId.Value,
                        PayingAccountId = rt.AccountId,
                        Amount = rt.Amount,
                        Date = rt.NextProcessDate
                    });

                    success = loanResult.IsSuccess;
                    if (!success)
                    {
                        var ex = new InvalidOperationException(
                            $"Loan payment failed for recurring tx {rt.Id}: {loanResult.Error.Description}");
                        failures.Add((rt.Id, rt.Description ?? "", ex));
                        _logger.LogError(ex, "Failed to process loan-linked recurring transaction {Id}", rt.Id);
                    }
                }
                else
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
                    success = result.IsSuccess;

                    if (!success)
                    {
                        var ex = new InvalidOperationException(
                            $"Application error for recurring tx {rt.Id}: {result.Error.Description}");
                        failures.Add((rt.Id, rt.Description ?? "", ex));
                        _logger.LogError(ex, "Failed to process recurring transaction {Id}", rt.Id);
                    }
                }

                if (success)
                {
                    rt.LastProcessedDate = rt.NextProcessDate;

                    if (rt.Frequency == RecurrenceFrequency.OneTime)
                    {
                        rt.IsActive = false;
                        _logger.LogInformation(
                            "One-time recurring transaction {Id} processed and deactivated — no further occurrences.",
                            rt.Id);
                    }
                    else
                    {
                        rt.NextProcessDate = CalculateNextDate(rt.NextProcessDate, rt.Frequency, rt.CustomDays);

                        if (rt.EndDate.HasValue && rt.NextProcessDate > rt.EndDate.Value)
                        {
                            rt.IsActive = false;
                            _logger.LogInformation(
                                "Recurring transaction {Id} deactivated — end date {EndDate} reached.",
                                rt.Id, rt.EndDate);
                        }

                        _logger.LogInformation(
                            "Recurring transaction {Id} processed. Next run: {NextDate}",
                            rt.Id, rt.NextProcessDate);
                    }

                    _context.RecurringTransactions.Update(rt);
                    await _context.SaveChangesAsync(default);
                    await dbTransaction.CommitAsync();
                    committed = true;
                }
                else
                {
                    await dbTransaction.RollbackAsync();
                    await DiscardUnsavedChangesAsync(rt);
                }
            }
            catch (Exception ex)
            {
                await dbTransaction.RollbackAsync();
                await DiscardUnsavedChangesAsync(rt);
                failures.Add((rt.Id, rt.Description ?? "", ex));
                _logger.LogError(ex, "Exception processing recurring transaction {Id}", rt.Id);
            }

            // Stop on the first failure so one broken item doesn't retry 12 times in a row.
            if (!committed) break;
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

    /// <summary>
    /// Transaction-scoped Postgres advisory lock for one recurring item (released automatically on
    /// commit/rollback). Returns true when there is no Postgres to lock against (unit tests).
    /// </summary>
    private async Task<bool> TryLockAsync(int recurringId)
    {
        var db = ((DbContext)_context).Database;
        if (db.ProviderName == null || !db.ProviderName.Contains("Npgsql", StringComparison.OrdinalIgnoreCase))
            return true;

        return await db.SqlQuery<bool>(
                $"SELECT pg_try_advisory_xact_lock(hashtext({"recurring-tx:" + recurringId})) AS \"Value\"")
            .SingleAsync();
    }

    /// <summary>
    /// After a rolled-back attempt the DbContext still tracks whatever that attempt touched: a new
    /// Transaction that was added before a funds check failed, an Account whose balance was changed in
    /// memory. The NEXT item's SaveChanges would silently persist those leftovers. Drop them, and
    /// reload the failed recurring item itself from the database.
    /// </summary>
    private async Task DiscardUnsavedChangesAsync(RecurringTransaction failed)
    {
        var ctx = (DbContext)_context;
        foreach (var entry in ctx.ChangeTracker.Entries().ToList())
        {
            if (entry.Entity is RecurringTransaction r)
            {
                if (ReferenceEquals(r, failed)) await entry.ReloadAsync();
                continue; // the other due items stay tracked, untouched
            }
            entry.State = EntityState.Detached;
        }
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

    private static DateTime CalculateNextCustomDate(DateTime currentUtc, RecurrenceDayOfWeek? customDays)
    {
        if (customDays is null or RecurrenceDayOfWeek.None)
            throw new InvalidOperationException(
                "Custom recurrence requires at least one day selected.");

        var currentLocal = currentUtc.ToLocal();

        for (int i = 1; i <= 7; i++)
        {
            var candidateLocal = currentLocal.AddDays(i);
            if ((customDays.Value & DayOfWeekToFlag(candidateLocal.DayOfWeek)) != 0)
                return candidateLocal.ToUtc();
        }

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
