using Application.Common.Models;
using Application.Contracts;
using Application.DTOs.RecurringTransactions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Application.Features.RecurringTransactions.Queries;

public record GetUpcomingObligationsQuery(string UserId, int DaysAhead = 30) : IRequest<Result<List<RecurringTransactionDto>>>;

public class GetUpcomingObligationsQueryHandler : IRequestHandler<GetUpcomingObligationsQuery, Result<List<RecurringTransactionDto>>>
{
    private readonly IApplicationDbContext _context;

    public GetUpcomingObligationsQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Result<List<RecurringTransactionDto>>> Handle(GetUpcomingObligationsQuery request, CancellationToken cancellationToken)
    {
        var cutoff = DateTime.UtcNow.AddDays(request.DaysAhead);

        var obligations = await _context.RecurringTransactions
            .Include(rt => rt.Account)
            .Include(rt => rt.TransactionCategory)
            .Where(rt => rt.UserId == request.UserId &&
                         rt.IsActive &&
                         rt.IsObligation &&
                         rt.NextProcessDate <= cutoff)
            .OrderBy(rt => rt.NextProcessDate)
            .Select(rt => new RecurringTransactionDto
            {
                Id = rt.Id,
                AccountId = rt.AccountId,
                AccountName = rt.Account.Name,
                TransactionCategoryId = rt.TransactionCategoryId,
                CategoryName = rt.TransactionCategory != null ? rt.TransactionCategory.Name : null,
                Description = rt.Description,
                Amount = rt.Amount,
                Type = rt.Type,
                Frequency = rt.Frequency,
                CustomDays = rt.CustomDays,
                StartDate = rt.StartDate,
                EndDate = rt.EndDate,
                NextProcessDate = rt.NextProcessDate,
                IsActive = rt.IsActive,
                LastProcessedDate = rt.LastProcessedDate,
                IsObligation = rt.IsObligation
            })
            .ToListAsync(cancellationToken);

        return Result.Success(obligations);
    }
}
