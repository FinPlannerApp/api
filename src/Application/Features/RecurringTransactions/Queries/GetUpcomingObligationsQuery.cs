using Application.Common.Models;
using Application.Contracts;
using Application.DTOs.RecurringTransactions;
using Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Application.Features.RecurringTransactions.Queries;

public record GetUpcomingObligationsQuery(string UserId, int DaysAhead = 30) : IRequest<Result<List<UpcomingObligationDto>>>;

public class GetUpcomingObligationsQueryHandler : IRequestHandler<GetUpcomingObligationsQuery, Result<List<UpcomingObligationDto>>>
{
    private readonly IApplicationDbContext _context;

    public GetUpcomingObligationsQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Result<List<UpcomingObligationDto>>> Handle(GetUpcomingObligationsQuery request, CancellationToken cancellationToken)
    {
        var cutoff = DateTime.UtcNow.AddDays(request.DaysAhead);
        var results = new List<UpcomingObligationDto>();

        // ── Source 1: recurring transactions explicitly flagged as obligations ──
        var recurringObligations = await _context.RecurringTransactions
            .Include(rt => rt.Account)
            .Where(rt => rt.UserId == request.UserId &&
                         rt.IsActive &&
                         rt.IsObligation &&
                         rt.NextProcessDate <= cutoff)
            .Select(rt => new UpcomingObligationDto
            {
                Description = rt.Description,
                AccountName = rt.Account.Name,
                Amount = rt.Amount,
                MinimumDueAmount = null,
                DueDate = rt.NextProcessDate,
                Source = "Recurring"
            })
            .ToListAsync(cancellationToken);

        results.AddRange(recurringObligations);

        // ── Source 2: credit card due dates, pulled directly from the account ──
        var today = DateTime.UtcNow.Date;
        var creditCardDueDates = await _context.Accounts
            .Include(a => a.CreditCardDetails)
            .Where(a => a.UserId == request.UserId &&
                        a.AccountCategory.AccountType == AccountType.CreditCard &&
                        a.CreditCardDetails != null &&
                        a.CreditCardDetails.DueDate != null &&
                        a.CreditCardDetails.DueDate >= today &&
                        a.CreditCardDetails.DueDate <= cutoff)
            .Select(a => new UpcomingObligationDto
            {
                Description = $"{a.Name} bill due",
                AccountName = a.Name,
                Amount = null,
                MinimumDueAmount = a.CreditCardDetails!.MinimumDueAmount,
                DueDate = a.CreditCardDetails!.DueDate!.Value,
                Source = "CreditCard"
            })
            .ToListAsync(cancellationToken);

        results.AddRange(creditCardDueDates);

        return Result.Success(results.OrderBy(r => r.DueDate).ToList());
    }
}
