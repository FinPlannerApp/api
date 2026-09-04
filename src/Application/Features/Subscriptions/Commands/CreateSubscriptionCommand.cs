using Application.Common.Helpers;
using Application.Common.Models;
using Application.Contracts;
using Domain.Entities;
using Domain.Enums;
using FluentValidation;
using MediatR;

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

        var nextProcessDate = CalculateFirstOccurrenceOnOrAfterToday(dto.StartDate, dto.Frequency);

        var recurringTransaction = new RecurringTransaction
        {
            UserId = request.UserId,
            AccountId = dto.AccountId,
            TransactionCategoryId = dto.CategoryId,
            Description = $"Subscription: {dto.Name}",
            Amount = dto.Amount,
            Type = TransactionType.Expense, // subscriptions are expenses
            Frequency = dto.Frequency,
            StartDate = dto.StartDate.EnsureUtc(), // the REAL historical start — unchanged, this is what fixes the display/age-calculation issue
            NextProcessDate = nextProcessDate.EnsureUtc(), // the actual next occurrence, never a past date
            IsActive = true,
            // A subscription is, by definition, a real financial
            // commitment — it should always surface in the obligation
            // system without requiring a separate manual toggle.
            IsObligation = true
        };

        var subscription = new Subscription
        {
            UserId = request.UserId,
            Name = dto.Name,
            Tag = dto.Tag,
            CancellationUrl = dto.CancellationUrl,
            RecurringTransaction = recurringTransaction // EF will wire up the FK
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
