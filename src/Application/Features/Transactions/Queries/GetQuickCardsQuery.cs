using Application.Common.Models;
using Application.Contracts;
using Application.DTOs.Transactions;
using Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Application.Features.Transactions.Queries;

public record GetQuickCardsQuery(string UserId, int Days = 120, int MinCount = 3, int Limit = 12) : IRequest<Result<List<QuickCardDto>>>;

/// <summary>
/// The user's most repeated entries: the same description, amount, type, category and account recorded at least
/// MinCount times in the last Days days. Only plain, hand-entered rows count (no transfers, loan/card legs,
/// interest, opening balances, adjustments, recurring or imported rows), so a card is something the user really types.
/// </summary>
public class GetQuickCardsQueryHandler : IRequestHandler<GetQuickCardsQuery, Result<List<QuickCardDto>>>
{
    private readonly IApplicationDbContext _context;
    public GetQuickCardsQueryHandler(IApplicationDbContext context) => _context = context;

    public async Task<Result<List<QuickCardDto>>> Handle(GetQuickCardsQuery request, CancellationToken ct)
    {
        var days = Math.Clamp(request.Days, 14, 365);
        var minCount = Math.Clamp(request.MinCount, 2, 20);
        var limit = Math.Clamp(request.Limit, 1, 30);
        var since = DateTime.UtcNow.AddDays(-days);

        var rows = await _context.Transactions.AsNoTracking()
            .Where(t => t.UserId == request.UserId && !t.IsDeleted && t.Date >= since
                        && t.Kind == TransactionKind.Normal && t.TransferGroupId == null
                        && !t.IsOpeningBalance && !t.IsBalanceAdjustment
                        && t.RecurringTransactionId == null && t.ImportRef == null
                        && !t.Account.IsArchived && !t.Account.IsDeleted)
            .Select(t => new { t.Description, t.Amount, t.Type, t.TransactionCategoryId, t.AccountId, t.Date })
            .ToListAsync(ct);

        var cards = rows
            .GroupBy(r => new { Desc = r.Description.Trim().ToLowerInvariant(), r.Amount, r.Type, r.TransactionCategoryId, r.AccountId })
            .Where(g => g.Count() >= minCount)
            .Select(g =>
            {
                var latest = g.OrderByDescending(x => x.Date).First();
                return new QuickCardDto
                {
                    Description = latest.Description.Trim(),
                    Amount = latest.Amount,
                    Type = latest.Type,
                    TransactionCategoryId = latest.TransactionCategoryId,
                    AccountId = latest.AccountId,
                    Count = g.Count(),
                    LastUsed = latest.Date
                };
            })
            .OrderByDescending(c => c.Count).ThenByDescending(c => c.LastUsed)
            .Take(limit)
            .ToList();

        return Result.Success(cards);
    }
}
