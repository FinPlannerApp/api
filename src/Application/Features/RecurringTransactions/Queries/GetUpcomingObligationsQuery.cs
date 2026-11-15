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
        var recurringRows = await _context.RecurringTransactions
            .Include(rt => rt.Account).ThenInclude(a => a.AccountCategory)
            .Where(rt => rt.UserId == request.UserId &&
                         rt.IsActive &&
                         rt.IsObligation &&
                         // A recurring item linked to a loan is that loan's auto-debit; the loan's
                         // own EMI row below represents it. Listing both would count the EMI twice.
                         rt.LinkedLoanAccountId == null &&
                         rt.NextProcessDate <= cutoff)
            .ToListAsync(cancellationToken);

        var recurringObligations = recurringRows.Select(rt =>
        {
            var type = rt.Account.AccountCategory.AccountType;
            var hitsCash =
                type == AccountType.CreditCard ||
                rt.Account.CashRole == CashRole.Spendable ||
                (rt.Account.CashRole == CashRole.Auto && (type == AccountType.Bank || type == AccountType.Cash));
            var dto = new UpcomingObligationDto
            {
                Description = rt.Description,
                AccountName = rt.Account.Name,
                Amount = rt.Amount,
                MinimumDueAmount = null,
                DueDate = rt.NextProcessDate,
                Source = "Recurring",
                ReducesAvailableCash = hitsCash
            };
            // Paid from a closed-loop wallet / non-liquid account: the wallet's own balance pays first, and only
            // the part it cannot cover has to be topped up from real cash. Without this a wallet-paid
            // subscription was either ignored entirely or counted in full.
            if (!hitsCash && rt.Type == TransactionType.Expense &&
                type != AccountType.Loan && !(rt.Account.AccountCategory.IsLiability))
            {
                var shortfall = Math.Max(0m, rt.Amount - Math.Max(0m, rt.Account.Balance));
                if (shortfall > 0)
                {
                    dto.ReducesAvailableCash = true;
                    dto.CashImpactAmount = shortfall;
                    dto.IsShortfallRisk = true;
                }
            }
            return dto;
        }).ToList();

        results.AddRange(recurringObligations);
        var todayForFlags = AppTimeZone.TodayLocal();
        foreach (var r in recurringObligations) r.IsOverdue = r.DueDate.Date < todayForFlags.Date;

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

            // Every statement that is still unpaid (not just the latest) is an obligation of its own;
            // a bill that is paid, or fully covered by payments, is not.
            var unpaid = await CreditCardBillMath.UnpaidBillsAsync(_context, account.Id, cancellationToken);
            var listed = false;
            foreach (var (bill, remaining) in unpaid)
            {
                if (!bill.DueDate.HasValue || bill.DueDate.Value > cutoff) continue;
                listed = true;
                // Overdue (due date already past) stays in the list on purpose — it is the
                // most urgent item, so it must not silently disappear.
                results.Add(new UpcomingObligationDto
                {
                    Description = unpaid.Count > 1
                        ? $"{account.Name} bill due ({bill.StatementDate:d MMM} statement)"
                        : $"{account.Name} bill due",
                    AccountName = account.Name,
                    Amount = remaining, // what is still unpaid, not the original statement amount
                    MinimumDueAmount = bill.MinimumDue,
                    DueDate = bill.DueDate.Value,
                    Source = "CreditCard",
                    IsOverdue = bill.DueDate.Value.Date < today.Date,
                    ReducesAvailableCash = false // already inside Real Available Cash via the card's balance
                });
            }

            if (listed) continue;
            if ((recentBill == null || recentBill.DueDate == null) &&
                     account.CreditCardDetails?.DueDate is { } staticDueDate &&
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
                    Source = "CreditCard",
                    ReducesAvailableCash = false
                });
            }
        }

        // ── Source 3: loan EMI due dates ─────────────────────────────────────
        var loanAccounts = await _context.Accounts
            .Include(a => a.LoanDetails)
            .Where(a => a.UserId == request.UserId && a.AccountCategory.AccountType == AccountType.Loan)
            .ToListAsync(cancellationToken);

        var autoPayLoanIds = (await _context.RecurringTransactions
            .Where(rt => rt.UserId == request.UserId && rt.IsActive && !rt.IsDeleted && rt.LinkedLoanAccountId != null)
            .Select(rt => rt.LinkedLoanAccountId!.Value)
            .ToListAsync(cancellationToken)).ToHashSet();

        foreach (var loan in loanAccounts)
        {
            if (loan.LoanDetails?.NextEmiDueDate == null || loan.LoanDetails.EmiAmount == null)
                continue;

            var dueDate = loan.LoanDetails.NextEmiDueDate.Value;
            if (dueDate > cutoff)
                continue; // overdue EMIs (dueDate < today) are kept and flagged below

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
                IsShortfallRisk = isShortfallRisk,
                IsOverdue = dueDate.Date < today.Date,
                ReducesAvailableCash = true,
                LoanAccountId = loan.Id,
                OverdueInstallments = CountDueInstallments(dueDate, today),
                IsAutoPay = autoPayLoanIds.Contains(loan.Id)
            });
        }

        // ── Source 4: recurring account-level charges — CC annual fee, bank periodic charges ──
        var ccAccountsWithFee = await _context.Accounts
            .Include(a => a.CreditCardDetails)
            .Where(a => a.UserId == request.UserId &&
                        a.AccountCategory.AccountType == AccountType.CreditCard &&
                        a.CreditCardDetails!.AnnualFee.HasValue &&
                        a.CreditCardDetails!.NextAnnualFeeDate.HasValue)
            .ToListAsync(cancellationToken);

        foreach (var account in ccAccountsWithFee)
        {
            var dueDate = account.CreditCardDetails!.NextAnnualFeeDate!.Value;
            if (dueDate < today || dueDate > cutoff)
                continue;

            results.Add(new UpcomingObligationDto
            {
                Description = $"{account.Name} annual fee",
                AccountName = account.Name,
                Amount = account.CreditCardDetails.AnnualFee,
                MinimumDueAmount = null,
                DueDate = dueDate,
                Source = "AccountCharge",
                ReducesAvailableCash = true
            });
        }

        var bankAccountsWithCharge = await _context.Accounts
            .Include(a => a.BankAccountDetails)
            .Where(a => a.UserId == request.UserId &&
                        a.AccountCategory.AccountType == AccountType.Bank &&
                        a.BankAccountDetails!.PeriodicChargeAmount.HasValue &&
                        a.BankAccountDetails!.NextPeriodicChargeDate.HasValue)
            .ToListAsync(cancellationToken);

        foreach (var account in bankAccountsWithCharge)
        {
            var dueDate = account.BankAccountDetails!.NextPeriodicChargeDate!.Value;
            if (dueDate < today || dueDate > cutoff)
                continue;

            results.Add(new UpcomingObligationDto
            {
                Description = $"{account.Name} account charge",
                AccountName = account.Name,
                Amount = account.BankAccountDetails.PeriodicChargeAmount,
                MinimumDueAmount = null,
                DueDate = dueDate,
                Source = "AccountCharge",
                ReducesAvailableCash = true
            });
        }

        return Result.Success(results.OrderBy(r => r.DueDate).ToList());
    }

    /// <summary>How many monthly EMIs have fallen due on or before today (0 when the first is still ahead).</summary>
    private static int CountDueInstallments(DateTime firstDue, DateTime today)
    {
        var n = 0;
        while (n < 36 && firstDue.AddMonths(n).Date <= today.Date) n++;
        return n;
    }
}
