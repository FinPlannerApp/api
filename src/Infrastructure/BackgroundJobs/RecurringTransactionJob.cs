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
            await using var dbTransaction = await ((DbContext)_context).Database.BeginTransactionAsync();

            try
            {
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
                }
                else
                {
                    await dbTransaction.RollbackAsync();
                }
            }
            catch (Exception ex)
            {
                await dbTransaction.RollbackAsync();
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
