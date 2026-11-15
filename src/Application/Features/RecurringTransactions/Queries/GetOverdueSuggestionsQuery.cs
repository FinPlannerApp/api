using Application.Common.Models;
using Application.Contracts;
using Application.DTOs.RecurringTransactions;
using Domain.Entities;
using Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Application.Features.RecurringTransactions.Queries;

public record GetOverdueSuggestionsQuery(string UserId) : IRequest<Result<List<OverdueSuggestionDto>>>;

/// <summary>
/// Looks at every overdue obligation and, for each one, at transactions recorded since shortly before it was due.
/// When one matches the amount (and the card, loan or name), it is offered as "this may already be paid".
/// Suggestions only: nothing is changed here.
/// </summary>
public class GetOverdueSuggestionsQueryHandler : IRequestHandler<GetOverdueSuggestionsQuery, Result<List<OverdueSuggestionDto>>>
{
    private readonly IApplicationDbContext _context;
    private readonly IMediator _mediator;

    public GetOverdueSuggestionsQueryHandler(IApplicationDbContext context, IMediator mediator)
    {
        _context = context;
        _mediator = mediator;
    }

    public async Task<Result<List<OverdueSuggestionDto>>> Handle(GetOverdueSuggestionsQuery request, CancellationToken ct)
    {
        var obligations = await _mediator.Send(new GetUpcomingObligationsQuery(request.UserId, 0), ct);
        if (obligations.IsFailure) return Result.Failure<List<OverdueSuggestionDto>>(obligations.Error);

        var overdue = obligations.Value
            .Where(o => o.IsOverdue && o.Amount is > 0 && (o.Source == "CreditCard" || o.Source == "Loan" || o.Source == "Recurring"))
            .Take(25)
            .ToList();

        var now = DateTime.UtcNow;
        var results = new List<OverdueSuggestionDto>();

        foreach (var o in overdue)
        {
            var amount = o.Amount!.Value;
            var from = o.DueDate.AddDays(o.Source == "CreditCard" ? -30 : -7);
            var accountId = o.Source == "Loan" ? o.LoanAccountId : o.AccountId;
            var low = amount * 0.98m;
            var high = amount * 1.02m;

            var query = _context.Transactions
                .Include(t => t.Account)
                .Where(t => t.UserId == request.UserId && t.Date >= from && t.Date <= now);

            Transaction? match = null;
            var confidence = "medium";
            var reason = string.Empty;

            // 1. A payment recorded on the card or loan itself (an income entry reduces what is owed).
            if ((o.Source == "CreditCard" || o.Source == "Loan") && accountId.HasValue)
            {
                var onAccount = query.Where(t => t.AccountId == accountId.Value && t.Type == TransactionType.Income);
                match = o.Source == "CreditCard"
                    ? await onAccount.Where(t => t.Amount >= low).OrderBy(t => t.Amount).ThenByDescending(t => t.Date).FirstOrDefaultAsync(ct)
                    : await onAccount.Where(t => t.Amount >= low && t.Amount <= high).OrderByDescending(t => t.Date).FirstOrDefaultAsync(ct);
                if (match != null)
                {
                    confidence = "high";
                    reason = $"A payment of {match.Amount:N0} on {match.Date:d MMM} on {o.AccountName} covers this.";
                }
            }

            // 2. An expense somewhere else with the same amount and the name in its description.
            if (match == null)
            {
                var token = FirstWord(o.AccountName) ?? FirstWord(o.Description);
                if (token != null)
                {
                    var t = token.ToLower();
                    var candidate = await query
                        .Where(x => x.Type == TransactionType.Expense && x.Amount >= low && x.Amount <= high &&
                                    (x.Description.ToLower().Contains(t) || (accountId.HasValue && x.AccountId == accountId.Value)))
                        .OrderByDescending(x => x.Date)
                        .FirstOrDefaultAsync(ct);
                    if (candidate != null)
                    {
                        match = candidate;
                        confidence = candidate.Description.ToLower().Contains(t) ? "high" : "medium";
                        reason = $"A {candidate.Amount:N0} payment on {candidate.Date:d MMM} (\"{candidate.Description}\") looks like this.";
                    }
                }
            }

            if (match == null) continue;

            results.Add(new OverdueSuggestionDto
            {
                Source = o.Source,
                Description = o.Description,
                AccountName = o.AccountName,
                AccountId = accountId,
                CreditCardBillId = o.CreditCardBillId,
                DueDate = o.DueDate,
                Amount = amount,
                Confidence = confidence,
                Reason = reason,
                CandidateTransactionId = match.Id,
                CandidateDate = match.Date,
                CandidateAmount = match.Amount,
                CandidateDescription = match.Description,
                CandidateAccountName = match.Account?.Name ?? string.Empty,
                CanMarkPaid = o.Source == "CreditCard" && o.CreditCardBillId.HasValue
            });
        }

        return Result.Success(results.OrderBy(r => r.Confidence == "high" ? 0 : 1).ThenBy(r => r.DueDate).ToList());
    }

    /// <summary>The first word of at least 4 letters (a bank, card or service name), used to find the name in a description.</summary>
    private static string? FirstWord(string? text) =>
        (text ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault(w => w.Length >= 4 && w.All(char.IsLetter));
}
