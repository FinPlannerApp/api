using Application.Common.Models;
using Application.Contracts;
using Application.DTOs.Transactions;
using Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Application.Features.Transactions.Queries;

public record GetDailySpendQuery(string UserId, DateTime StartDate, DateTime EndDate) : IRequest<Result<List<DailySpendDto>>>;

/// <summary>Expense total per calendar day for the spending heatmap. Transfers, loan/card legs, opening balances and
/// balance adjustments are not spending, so they are left out (same rule as every other expense total).</summary>
public class GetDailySpendQueryHandler : IRequestHandler<GetDailySpendQuery, Result<List<DailySpendDto>>>
{
    private readonly IApplicationDbContext _context;
    public GetDailySpendQueryHandler(IApplicationDbContext context) => _context = context;

    public async Task<Result<List<DailySpendDto>>> Handle(GetDailySpendQuery request, CancellationToken ct)
    {
        var start = request.StartDate.Date;
        var end = request.EndDate.Date;
        if (end < start)
            return Result.Failure<List<DailySpendDto>>(new Error("Transaction.InvalidRange", "End date is before the start date."));
        if ((end - start).TotalDays > 400)
            return Result.Failure<List<DailySpendDto>>(new Error("Transaction.RangeTooLarge", "Choose a range of at most 400 days."));
        var endExclusive = end.AddDays(1);

        var rows = await _context.Transactions.AsNoTracking()
            .Where(t => t.UserId == request.UserId && !t.IsDeleted
                        && t.Type == TransactionType.Expense && t.Kind == TransactionKind.Normal
                        && t.TransferGroupId == null && !t.IsOpeningBalance && !t.IsBalanceAdjustment
                        && t.Date >= start && t.Date < endExclusive
                        && !t.Account.IsDeleted)
            .Select(t => new { t.Date, t.Amount })
            .ToListAsync(ct);

        var days = rows
            .GroupBy(r => r.Date.Date)
            .Select(g => new DailySpendDto { Date = g.Key, Total = g.Sum(x => x.Amount), Count = g.Count() })
            .OrderBy(d => d.Date)
            .ToList();
        return Result.Success(days);
    }
}
