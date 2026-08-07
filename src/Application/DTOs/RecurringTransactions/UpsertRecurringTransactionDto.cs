using Domain.Enums;
using FluentValidation;

namespace Application.DTOs.RecurringTransactions;

public class UpsertRecurringTransactionDto
{
    public int? Id { get; set; }
    public int AccountId { get; set; }
    public int? TransactionCategoryId { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public TransactionType Type { get; set; }
    public RecurrenceFrequency Frequency { get; set; }

    /// <summary>Only meaningful when Frequency == Custom (e.g. Monday | Wednesday | Friday).</summary>
    public RecurrenceDayOfWeek? CustomDays { get; set; }

    public DateTime StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsObligation { get; set; } = false;
}

public class UpsertRecurringTransactionDtoValidator : AbstractValidator<UpsertRecurringTransactionDto>
{
    public UpsertRecurringTransactionDtoValidator()
    {
        RuleFor(x => x.Description).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Amount).GreaterThan(0);
        RuleFor(x => x.AccountId).NotEmpty();
        RuleFor(x => x.Frequency).IsInEnum();
        RuleFor(x => x.StartDate).NotEmpty();

        // Custom frequency requires at least one day selected — every other
        // frequency ignores CustomDays entirely, so no rule needed for them.
        RuleFor(x => x.CustomDays)
            .NotNull()
            .WithMessage("Select at least one day for a custom recurrence.")
            .Must(days => days != RecurrenceDayOfWeek.None)
            .WithMessage("Select at least one day for a custom recurrence.")
            .When(x => x.Frequency == RecurrenceFrequency.Custom);
    }
}
