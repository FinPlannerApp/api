using Application.Common.Helpers;
using Application.Common.Models;
using Application.Contracts;
using Application.DTOs.RecurringTransactions;
using Domain.Entities;
using Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Application.Features.RecurringTransactions.Commands;

public record UpsertRecurringTransactionCommand(string UserId, int? Id, UpsertRecurringTransactionDto Dto) : IRequest<Result<RecurringTransactionDto>>;

public class UpsertRecurringTransactionCommandHandler : IRequestHandler<UpsertRecurringTransactionCommand, Result<RecurringTransactionDto>>
{
    private readonly IApplicationDbContext _context;

    public UpsertRecurringTransactionCommandHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Result<RecurringTransactionDto>> Handle(UpsertRecurringTransactionCommand request, CancellationToken cancellationToken)
    {
        // Verify account exists and belongs to user
        var account = await _context.Accounts
            .FirstOrDefaultAsync(a => a.Id == request.Dto.AccountId && a.UserId == request.UserId, cancellationToken);
        
        if (account == null)
        {
            return Result.Failure<RecurringTransactionDto>(new Error("Account.NotFound", "Account not found or access denied."));
        }

        RecurringTransaction? entity;
        bool isNew = !(request.Id.HasValue && request.Id.Value > 0);

        // Captured only when editing — nothing to compare against for
        // a brand-new entity, and isNew already covers that case below.
        RecurrenceFrequency oldFrequency = default;
        DateTime oldStartDate = default;
        RecurrenceDayOfWeek? oldCustomDays = null;

        if (request.Id.HasValue && request.Id.Value > 0)
        {
            entity = await _context.RecurringTransactions
                .FirstOrDefaultAsync(rt => rt.Id == request.Id && rt.UserId == request.UserId, cancellationToken);

            if (entity == null)
            {
                return Result.Failure<RecurringTransactionDto>(new Error("RecurringTransaction.NotFound", "Recurring transaction not found."));
            }

            oldFrequency = entity.Frequency;
            oldStartDate = entity.StartDate;
            oldCustomDays = entity.CustomDays;
        }
        else
        {
            entity = new RecurringTransaction
            {
                UserId = request.UserId,
                Description = request.Dto.Description // Set required property
            };
            _context.RecurringTransactions.Add(entity);
        }

        // entity is guaranteed non-null here
        entity.AccountId = request.Dto.AccountId;
        entity.TransactionCategoryId = request.Dto.TransactionCategoryId;
        entity.Description = request.Dto.Description;
        entity.Amount = request.Dto.Amount;
        entity.Type = request.Dto.Type;
        entity.Frequency = request.Dto.Frequency;
        entity.CustomDays = request.Dto.Frequency == RecurrenceFrequency.Custom
            ? request.Dto.CustomDays
            : null; // clear stale CustomDays if frequency changed away from Custom
        entity.StartDate = request.Dto.StartDate;
        entity.EndDate = request.Dto.EndDate;
        entity.IsActive = request.Dto.IsActive;
        entity.IsObligation = request.Dto.IsObligation;
        if (request.Dto.LinkedLoanAccountId.HasValue)
        {
            var loanBelongsToUser = await _context.Accounts
                .AnyAsync(a => a.Id == request.Dto.LinkedLoanAccountId.Value && a.UserId == request.UserId, cancellationToken);
            if (!loanBelongsToUser)
                return Result.Failure<RecurringTransactionDto>(new Error(
                    "RecurringTransaction.InvalidLoanAccount", "That loan account doesn't belong to you."));
        }

        entity.LinkedLoanAccountId = request.Dto.LinkedLoanAccountId;

        if (isNew)
        {
            // Unchanged behavior for genuinely new entries — StartDate
            // is used directly, without rolling forward past today.
            // Deliberately different from CreateSubscriptionCommand's
            // own roll-forward: a new recurring transaction may
            // legitimately want to catch up on a real past StartDate,
            // where a subscription specifically avoids that to prevent
            // a surprise back-dated charge on save.
            entity.NextProcessDate = entity.StartDate;
        }
        else
        {
            bool scheduleChanged =
                oldFrequency != entity.Frequency ||
                oldStartDate != entity.StartDate ||
                oldCustomDays != entity.CustomDays;

            if (scheduleChanged)
            {
                // A live edit, not initial setup — roll forward to the
                // first valid occurrence on or after today under the
                // NEW schedule, rather than leaving NextProcessDate
                // pointing at a date computed under the old one.
                entity.NextProcessDate = RecurrenceCalculator.CalculateNextOccurrenceOnOrAfter(
                    entity.StartDate, entity.Frequency, entity.CustomDays, DateTime.UtcNow);
            }
        }

        await _context.SaveChangesAsync(cancellationToken);

        // Fetch names manually for the DTO
        var accountName = await _context.Accounts
            .Where(a => a.Id == entity.AccountId)
            .Select(a => a.Name)
            .FirstOrDefaultAsync(cancellationToken) ?? string.Empty;

        string? categoryName = null;
        if (entity.TransactionCategoryId.HasValue)
        {
            categoryName = await _context.TransactionCategories
                .Where(tc => tc.Id == entity.TransactionCategoryId)
                .Select(tc => tc.Name)
                .FirstOrDefaultAsync(cancellationToken);
        }

        var resultDto = new RecurringTransactionDto
        {
            Id = entity.Id,
            AccountId = entity.AccountId,
            AccountName = accountName,
            TransactionCategoryId = entity.TransactionCategoryId,
            CategoryName = categoryName,
            Description = entity.Description,
            Amount = entity.Amount,
            Type = entity.Type,
            Frequency = entity.Frequency,
            CustomDays = entity.CustomDays,
            StartDate = entity.StartDate,
            EndDate = entity.EndDate,
            NextProcessDate = entity.NextProcessDate,
            IsActive = entity.IsActive,
            LastProcessedDate = entity.LastProcessedDate,
            IsObligation = entity.IsObligation,
            LinkedLoanAccountId = entity.LinkedLoanAccountId
        };

        return Result.Success(resultDto);
    }
}
