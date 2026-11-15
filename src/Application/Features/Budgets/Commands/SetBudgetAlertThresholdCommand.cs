using Application.Common.Models;
using Application.Contracts;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Application.Features.Budgets.Commands;

/// <summary>Sets (50–100) or clears (null → use the default) a single budget's alert level without touching its other fields.</summary>
public record SetBudgetAlertThresholdCommand(string UserId, int BudgetId, int? ThresholdPercent) : IRequest<Result<int?>>;

public class SetBudgetAlertThresholdCommandHandler : IRequestHandler<SetBudgetAlertThresholdCommand, Result<int?>>
{
    private readonly IApplicationDbContext _context;

    public SetBudgetAlertThresholdCommandHandler(IApplicationDbContext context) => _context = context;

    public async Task<Result<int?>> Handle(SetBudgetAlertThresholdCommand request, CancellationToken ct)
    {
        if (request.ThresholdPercent is < 50 or > 100)
            return Result.Failure<int?>(new Error("Budget.InvalidThreshold", "Alert level must be between 50% and 100%."));

        var budget = await _context.Budgets.FirstOrDefaultAsync(b => b.Id == request.BudgetId && b.UserId == request.UserId, ct);
        if (budget == null)
            return Result.Failure<int?>(new Error("Budget.NotFound", "Budget not found."));

        budget.AlertThresholdPercent = request.ThresholdPercent;
        await _context.SaveChangesAsync(ct);
        return Result<int?>.Success(budget.AlertThresholdPercent);
    }
}
