using Application.Common.Models;
using Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Application.Contracts;

namespace Application.Features.Subscriptions.Queries;

public class SubscriptionDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string? Tag { get; set; }
    public string? CancellationUrl { get; set; }
    public DateTime NextProcessDate { get; set; }
    public string Frequency { get; set; } = string.Empty;
    public int RecurringTransactionId { get; set; }
}

public record GetActiveSubscriptionsQuery(string UserId) : IRequest<Result<List<SubscriptionDto>>>;

public class GetActiveSubscriptionsQueryHandler : IRequestHandler<GetActiveSubscriptionsQuery, Result<List<SubscriptionDto>>>
{
    private readonly IApplicationDbContext _context;

    public GetActiveSubscriptionsQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Result<List<SubscriptionDto>>> Handle(GetActiveSubscriptionsQuery request, CancellationToken cancellationToken)
    {
        var subscriptions = await _context.Subscriptions
            .Include(s => s.RecurringTransaction)
            .Where(s => s.UserId == request.UserId && s.RecurringTransaction.IsActive)
            .Select(s => new SubscriptionDto
            {
                Id = s.Id,
                Name = s.Name,
                Amount = s.RecurringTransaction.Amount,
                Tag = s.Tag,
                CancellationUrl = s.CancellationUrl,
                NextProcessDate = s.RecurringTransaction.NextProcessDate,
                Frequency = s.RecurringTransaction.Frequency.ToString(),
                RecurringTransactionId = s.RecurringTransactionId
            })
            .ToListAsync(cancellationToken);

        return Result.Success(subscriptions);
    }
}
