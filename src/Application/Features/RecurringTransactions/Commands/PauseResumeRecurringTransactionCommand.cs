using Application.Common.Models;
using Application.Contracts;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Application.Features.RecurringTransactions.Commands;

public record PauseRecurringTransactionCommand(string UserId, int Id) : IRequest<Result<bool>>;
public record ResumeRecurringTransactionCommand(string UserId, int Id) : IRequest<Result<bool>>;

public class PauseRecurringTransactionCommandHandler : IRequestHandler<PauseRecurringTransactionCommand, Result<bool>>
{
    private readonly IApplicationDbContext _context;

    public PauseRecurringTransactionCommandHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Result<bool>> Handle(PauseRecurringTransactionCommand request, CancellationToken cancellationToken)
    {
        var entity = await _context.RecurringTransactions
            .FirstOrDefaultAsync(rt => rt.Id == request.Id && rt.UserId == request.UserId, cancellationToken);

        if (entity == null)
            return Result.Failure<bool>(new Error("RecurringTransaction.NotFound", "Recurring transaction not found."));

        entity.IsActive = false;
        _context.RecurringTransactions.Update(entity);
        await _context.SaveChangesAsync(cancellationToken);

        return Result.Success(true);
    }
}

public class ResumeRecurringTransactionCommandHandler : IRequestHandler<ResumeRecurringTransactionCommand, Result<bool>>
{
    private readonly IApplicationDbContext _context;

    public ResumeRecurringTransactionCommandHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Result<bool>> Handle(ResumeRecurringTransactionCommand request, CancellationToken cancellationToken)
    {
        var entity = await _context.RecurringTransactions
            .FirstOrDefaultAsync(rt => rt.Id == request.Id && rt.UserId == request.UserId, cancellationToken);

        if (entity == null)
            return Result.Failure<bool>(new Error("RecurringTransaction.NotFound", "Recurring transaction not found."));

        // Resuming doesn't move NextProcessDate forward — if it's in the
        // past (paused for a while), the recurring job will simply catch
        // it up on its next run, same as any other overdue recurring
        // transaction. Not silently skipping missed occurrences without
        // the user seeing them.
        entity.IsActive = true;
        _context.RecurringTransactions.Update(entity);
        await _context.SaveChangesAsync(cancellationToken);

        return Result.Success(true);
    }
}
