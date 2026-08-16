using Application.Common.Helpers;
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

        // ── Source 2: credit card bills — prefers a RECORDED bill (the
        // real amount, includes interest/fees) over the static
        // DueDate/MinimumDueAmount fields, which only ever reflected a
        // rough estimate anyway ──
        var today = AppTimeZone.TodayLocal();

        var creditCardAccounts = await _context.Accounts
            .Include(a => a.CreditCardDetails)
            .Where(a => a.UserId == request.UserId && a.AccountCategory.AccountType == AccountType.CreditCard)
            .ToListAsync(cancellationToken);

        foreach (var account in creditCardAccounts)
        {
            // Most recent recorded bill for this account, if any.
            var recentBill = await _context.CreditCardBills
                .Where(b => b.AccountId == account.Id)
                .OrderByDescending(b => b.StatementDate)
                .FirstOrDefaultAsync(cancellationToken);

            if (recentBill != null && recentBill.DueDate.HasValue &&
                recentBill.DueDate.Value >= today && recentBill.DueDate.Value <= cutoff)
            {
                results.Add(new UpcomingObligationDto
                {
                    Description = $"{account.Name} bill due",
                    AccountName = account.Name,
                    Amount = recentBill.BillAmount, // the real recorded amount, not an estimate
                    MinimumDueAmount = recentBill.MinimumDue,
                    DueDate = recentBill.DueDate.Value,
                    Source = "CreditCard"
                });
            }
            else if (account.CreditCardDetails?.DueDate is { } staticDueDate &&
                     staticDueDate >= today && staticDueDate <= cutoff)
            {
                // Fallback — no bill recorded for this cycle yet, use
                // whatever's set on the account itself, same as before
                // this feature existed.
                results.Add(new UpcomingObligationDto
                {
                    Description = $"{account.Name} bill due",
                    AccountName = account.Name,
                    Amount = null,
                    MinimumDueAmount = account.CreditCardDetails.MinimumDueAmount,
                    DueDate = staticDueDate,
                    Source = "CreditCard"
                });
            }
        }

        // ── Source 3: loan EMI due dates ─────────────────────────────────────
        var loanAccounts = await _context.Accounts
            .Include(a => a.LoanDetails)
            .Where(a => a.UserId == request.UserId && a.AccountCategory.AccountType == AccountType.Loan)
            .ToListAsync(cancellationToken);

        foreach (var loan in loanAccounts)
        {
            if (loan.LoanDetails?.NextEmiDueDate == null || loan.LoanDetails.EmiAmount == null)
                continue;

            var dueDate = loan.LoanDetails.NextEmiDueDate.Value;
            if (dueDate < today || dueDate > cutoff)
                continue;

            bool isShortfallRisk = false;
            if (loan.LoanDetails.DesignatedPayingAccountId.HasValue)
            {
                var payingAccount = await _context.Accounts
                    .FirstOrDefaultAsync(a => a.Id == loan.LoanDetails.DesignatedPayingAccountId.Value, cancellationToken);

                if (payingAccount != null && payingAccount.Balance < loan.LoanDetails.EmiAmount.Value)
                {
                    isShortfallRisk = true;
                }
            }

            results.Add(new UpcomingObligationDto
            {
                Description = $"{loan.Name} EMI due",
                AccountName = loan.Name,
                Amount = loan.LoanDetails.EmiAmount.Value,
                MinimumDueAmount = null,
                DueDate = dueDate,
                Source = "Loan",
                IsShortfallRisk = isShortfallRisk
            });
        }

        return Result.Success(results.OrderBy(r => r.DueDate).ToList());
    }
}
