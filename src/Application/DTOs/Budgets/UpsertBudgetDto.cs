using Domain.Enums;
using FluentValidation;

namespace Application.DTOs.Budgets;

public class UpsertBudgetDto
{
    public int? Id { get; set; }
    public int? TransactionCategoryId { get; set; }
    public decimal Amount { get; set; }
    public BudgetPeriod Period { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public int? AlertThresholdPercent { get; set; }
}

public class UpsertBudgetDtoValidator : AbstractValidator<UpsertBudgetDto>
{
    public UpsertBudgetDtoValidator()
    {
        RuleFor(x => x.Amount).GreaterThan(0).WithMessage("Budget amount must be greater than zero.");
        RuleFor(x => x.StartDate).NotEmpty().WithMessage("Start date is required.");
        RuleFor(x => x.Period).IsInEnum().WithMessage("Invalid budget period.");
        RuleFor(x => x.AlertThresholdPercent).InclusiveBetween(50, 100).When(x => x.AlertThresholdPercent.HasValue)
            .WithMessage("Alert level must be between 50% and 100%.");
    }
}
