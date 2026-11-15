using FluentValidation;
using Application.DTOs.Auth;
using Application.DTOs.Transactions;

namespace Application.Validators;

public class RegisterUserDtoValidator : AbstractValidator<RegisterUserDto>
{
    public RegisterUserDtoValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.UserName).NotEmpty().MinimumLength(3);
        RuleFor(x => x.Password).NotEmpty().MinimumLength(6)
            .Matches("[A-Z]").WithMessage("Password must contain one uppercase letter.")
            .Matches("[a-z]").WithMessage("Password must contain one lowercase letter.")
            .Matches("[0-9]").WithMessage("Password must contain one number.");
        RuleFor(x => x.DateOfBirth).LessThan(DateTime.UtcNow).WithMessage("Date of birth must be in the past.");
    }
}

public class UpsertTransactionDtoValidator : AbstractValidator<UpsertTransactionDto>
{
    public UpsertTransactionDtoValidator()
    {
        RuleFor(x => x.Description).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Amount).GreaterThan(0).LessThan(1_000_000_000_000m)
            .PrecisionScale(18, 2, true).WithMessage("Amount can have at most 2 decimal places.");
        RuleFor(x => x.Date).NotEmpty();
        RuleFor(x => x.Type).IsInEnum();
    }
}

/// <summary>
/// MediatR only runs validators written for the command itself, so the DTO rules above were never
/// executed. This wires them into the pipeline for the single-add command.
/// </summary>
public class UpsertTransactionCommandValidator : AbstractValidator<Application.Features.Transactions.Commands.UpsertTransactionCommand>
{
    public UpsertTransactionCommandValidator()
    {
        RuleFor(x => x.Transaction).NotNull().SetValidator(new UpsertTransactionDtoValidator());
    }
}

public class BulkUpsertTransactionsCommandValidator : AbstractValidator<Application.Features.Transactions.Commands.BulkUpsertTransactionsCommand>
{
    public BulkUpsertTransactionsCommandValidator()
    {
        RuleFor(x => x.Payload.Transactions).NotEmpty().Must(t => t.Count <= 500)
            .WithMessage("At most 500 rows can be saved at once.");
    }
}