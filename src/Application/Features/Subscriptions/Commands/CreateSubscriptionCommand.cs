using Application.Common.Helpers;
using Application.Common.Models;
using Application.Contracts;
using Domain.Entities;
using Domain.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Application.Features.Subscriptions.Commands;

public class CreateSubscriptionDto
{
    public required string Name { get; set; }
    public decimal Amount { get; set; }
    public int AccountId { get; set; }
    public int? CategoryId { get; set; }
    public RecurrenceFrequency Frequency { get; set; }
    public DateTime StartDate { get; set; }
    public string? Tag { get; set; }
    public string? CancellationUrl { get; set; }
}

public record CreateSubscriptionCommand(string UserId, CreateSubscriptionDto Dto) : IRequest<Result<int>>;

public class CreateSubscriptionCommandValidator : AbstractValidator<CreateSubscriptionCommand>
{
    public CreateSubscriptionCommandValidator()
    {
        RuleFor(x => x.Dto.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Dto.Amount).GreaterThan(0);
        RuleFor(x => x.Dto.AccountId).GreaterThan(0);
    }
}

public class CreateSubscriptionCommandHandler : IRequestHandler<CreateSubscriptionCommand, Result<int>>
{
    private readonly IApplicationDbContext _context;

    public CreateSubscriptionCommandHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Result<int>> Handle(CreateSubscriptionCommand request, CancellationToken cancellationToken)
    {
        var dto = request.Dto;
        var trimmedName = dto.Name?.Trim() ?? string.Empty;

        // 1. Verify target account exists and belongs to user
        var account = await _context.Accounts
            .FirstOrDefaultAsync(a => a.Id == dto.AccountId && a.UserId == request.UserId && !a.IsDeleted, cancellationToken);
        if (account == null)
        {
            return Result.Failure<int>(new Error("Account.NotFound", "The selected account was not found or is unavailable."));
        }

        // 2. Validate category if provided
        if (dto.CategoryId.HasValue)
        {
            var categoryExists = await _context.TransactionCategories
                .AnyAsync(c => c.Id == dto.CategoryId.Value && (c.UserId == request.UserId || c.UserId == null) && !c.IsDeleted, cancellationToken);
            if (!categoryExists)
            {
                dto.CategoryId = null;
            }
        }

        var nextProcessDate = CalculateFirstOccurrenceOnOrAfterToday(dto.StartDate, dto.Frequency);

        // 3. Check for existing subscription by name (including soft-deleted or paused) to avoid duplicate key errors
        var existing = await _context.Subscriptions
            .IgnoreQueryFilters()
            .Include(s => s.RecurringTransaction)
            .FirstOrDefaultAsync(s => s.UserId == request.UserId && s.Name.ToLower() == trimmedName.ToLower(), cancellationToken);

        if (existing != null)
        {
            // If already active in subscriptions list:
            if (!existing.IsDeleted && existing.RecurringTransaction != null && existing.RecurringTransaction.IsActive && !existing.RecurringTransaction.IsDeleted)
            {
                return Result.Failure<int>(new Error("Subscription.Duplicate", $"A subscription named '{trimmedName}' already exists."));
            }

            // Revive / reactivate existing subscription and its recurring transaction
            existing.IsDeleted = false;
            existing.DeletedAt = null;
            existing.Name = trimmedName;
            existing.Tag = dto.Tag?.Trim();
            existing.CancellationUrl = dto.CancellationUrl?.Trim();

            if (existing.RecurringTransaction != null)
            {
                existing.RecurringTransaction.IsDeleted = false;
                existing.RecurringTransaction.DeletedAt = null;
                existing.RecurringTransaction.AccountId = dto.AccountId;
                existing.RecurringTransaction.TransactionCategoryId = dto.CategoryId;
                existing.RecurringTransaction.Description = $"Subscription: {trimmedName}";
                existing.RecurringTransaction.Amount = dto.Amount;
                existing.RecurringTransaction.Frequency = dto.Frequency;
                existing.RecurringTransaction.StartDate = dto.StartDate.EnsureUtc();
                existing.RecurringTransaction.NextProcessDate = nextProcessDate.EnsureUtc();
                existing.RecurringTransaction.IsActive = true;
                existing.RecurringTransaction.IsObligation = true;
            }
            else
            {
                var newRt = new RecurringTransaction
                {
                    UserId = request.UserId,
                    AccountId = dto.AccountId,
                    TransactionCategoryId = dto.CategoryId,
                    Description = $"Subscription: {trimmedName}",
                    Amount = dto.Amount,
                    Type = TransactionType.Expense,
                    Frequency = dto.Frequency,
                    StartDate = dto.StartDate.EnsureUtc(),
                    NextProcessDate = nextProcessDate.EnsureUtc(),
                    IsActive = true,
                    IsObligation = true
                };
                _context.RecurringTransactions.Add(newRt);
                await _context.SaveChangesAsync(cancellationToken);
                existing.RecurringTransactionId = newRt.Id;
            }

            await _context.SaveChangesAsync(cancellationToken);
            return Result.Success(existing.Id);
        }

        // 4. Create new recurring transaction first to guarantee foreign key integrity
        var recurringTransaction = new RecurringTransaction
        {
            UserId = request.UserId,
            AccountId = dto.AccountId,
            TransactionCategoryId = dto.CategoryId,
            Description = $"Subscription: {trimmedName}",
            Amount = dto.Amount,
            Type = TransactionType.Expense,
            Frequency = dto.Frequency,
            StartDate = dto.StartDate.EnsureUtc(),
            NextProcessDate = nextProcessDate.EnsureUtc(),
            IsActive = true,
            IsObligation = true
        };

        _context.RecurringTransactions.Add(recurringTransaction);
        await _context.SaveChangesAsync(cancellationToken);

        var subscription = new Subscription
        {
            UserId = request.UserId,
            Name = trimmedName,
            Tag = dto.Tag?.Trim(),
            CancellationUrl = dto.CancellationUrl?.Trim(),
            RecurringTransactionId = recurringTransaction.Id
        };

        _context.Subscriptions.Add(subscription);
        await _context.SaveChangesAsync(cancellationToken);

        return Result.Success(subscription.Id);
    }

    /// <summary>
    /// If StartDate is in the past, rolls forward to the first real
    /// occurrence on or after today — without this, a subscription
    /// entered with a genuine past start date would look immediately
    /// overdue to the recurring job and generate a back-dated charge the
    /// moment it saves. StartDate itself stays untouched; only
    /// NextProcessDate is adjusted.
    /// </summary>
    private static DateTime CalculateFirstOccurrenceOnOrAfterToday(DateTime startDate, RecurrenceFrequency frequency)
    {
        var next = startDate;
        var today = DateTime.UtcNow;
        var safetyLimit = 10000; // defends against a degenerate input looping indefinitely, never realistically hit

        while (next < today && safetyLimit-- > 0)
        {
            next = frequency switch
            {
                RecurrenceFrequency.Daily => next.AddDays(1),
                RecurrenceFrequency.Weekly => next.AddDays(7),
                RecurrenceFrequency.Monthly => next.AddMonths(1),
                RecurrenceFrequency.Yearly => next.AddYears(1),
                _ => today // Custom/OneTime frequencies aren't really
                           // meaningful for a subscription — fall back to
                           // today rather than looping on a case this
                           // method isn't designed to roll forward
            };
        }

        return next;
    }
}
