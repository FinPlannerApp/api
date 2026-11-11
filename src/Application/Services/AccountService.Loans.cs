using Application.Common.Helpers;
using Application.Common.Models;
using Application.Contracts;
using Application.DTOs.Accounts;
using Domain.Entities;
using Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Application.Services;

/// <summary>Loan payments, due-date handling and amortisation.</summary>
public partial class AccountService
{
    public async Task<Result<LoanPaymentResultDto>> MakeLoanPaymentAsync(string userId, MakeLoanPaymentDto dto)
    {
        if (dto.LoanAccountId == dto.PayingAccountId)
            return Result.Failure<LoanPaymentResultDto>(new Error("LoanPayment.SameAccount", "The loan and the paying account can't be the same account."));

        if (dto.Amount <= 0)
            return Result.Failure<LoanPaymentResultDto>(new Error("LoanPayment.InvalidAmount", "Payment amount must be greater than zero."));

        var loanAccount = await _context.Accounts
            .Include(a => a.AccountCategory)
            .Include(a => a.LoanDetails)
            .FirstOrDefaultAsync(a => a.Id == dto.LoanAccountId);

        if (loanAccount == null || loanAccount.UserId != userId)
            return Result.Failure<LoanPaymentResultDto>(new Error("Account.NotFound", "Loan account not found."));

        if (loanAccount.AccountCategory?.AccountType != AccountType.Loan || loanAccount.LoanDetails == null)
            return Result.Failure<LoanPaymentResultDto>(new Error("LoanPayment.NotALoan", "This account isn't set up as a loan — check its category's account type."));

        var payingAccount = await _context.Accounts
            .Include(a => a.AccountCategory)
            .FirstOrDefaultAsync(a => a.Id == dto.PayingAccountId);

        if (payingAccount == null || payingAccount.UserId != userId)
            return Result.Failure<LoanPaymentResultDto>(new Error("Account.NotFound", "Paying account not found."));

        // The negative-balance guard, same reasoning as everywhere else
        // it's applied — a payment shouldn't be allowed to push a
        // non-liability paying account negative.
        bool payingAccountIsLiability = payingAccount.AccountCategory?.IsLiability ?? false;
        if (!payingAccountIsLiability && payingAccount.Balance < dto.Amount)
        {
            return Result.Failure<LoanPaymentResultDto>(new Error(
                "LoanPayment.InsufficientFunds",
                $"Insufficient balance. Available: {payingAccount.Balance:F2}, Requested: {dto.Amount:F2}."));
        }

        // Loan balances are stored negative (debt) — same convention as
        // every liability account in this app.
        var outstandingBalance = Math.Abs(loanAccount.Balance);

        // Compute what a full payoff would actually cost (remaining
        // principal + this period's interest) BEFORE splitting the
        // requested amount — needed to validate against, not to use in
        // the split itself.
        var monthlyRate = (loanAccount.LoanDetails.InterestRate ?? 0) / 100m / 12m;
        var thisMonthsInterest = Math.Round(outstandingBalance * monthlyRate, 2);
        var maxValidPayment = outstandingBalance + thisMonthsInterest;

        if (dto.Amount > maxValidPayment)
        {
            return Result.Failure<LoanPaymentResultDto>(new Error(
                "LoanPayment.ExceedsPayoff",
                $"This payment (₹{dto.Amount:F2}) is more than what's needed to fully pay off this loan " +
                $"(₹{maxValidPayment:F2} — ₹{outstandingBalance:F2} principal + ₹{thisMonthsInterest:F2} interest). " +
                "Enter the exact payoff amount if you're closing this loan out, or a smaller amount for a regular payment."));
        }

        var (interestPortion, principalPortion) = LoanAmortization.CalculateSplit(
            outstandingBalance, loanAccount.LoanDetails.InterestRate ?? 0, dto.Amount);

        // ── Interest portion: a real expense, full stop ──────────────────────
        if (interestPortion > 0)
        {
            var interestTransaction = new Transaction
            {
                Description = $"Interest — {loanAccount.Name}",
                Amount = interestPortion,
                Date = dto.Date,
                Type = TransactionType.Expense,
                AccountId = dto.PayingAccountId,
                UserId = userId,
                Kind = TransactionKind.LoanInterest
                // Deliberately not TransferGroupId-linked — this portion
                // genuinely IS an expense, the whole point of this method
                // is making sure only THIS part counts as one.
            };
            _context.Transactions.Add(interestTransaction);
            payingAccount.Balance -= interestPortion;
        }

        // ── Principal portion: debt repayment, not spending — same
        // TransferGroupId pattern CreateTransferAsync already uses ─────────
        if (principalPortion > 0)
        {
            var transferGroupId = Guid.NewGuid();

            var principalExpenseLeg = new Transaction
            {
                Description = $"Loan principal payment — {loanAccount.Name}",
                Amount = principalPortion,
                Date = dto.Date,
                Type = TransactionType.Expense,
                AccountId = dto.PayingAccountId,
                UserId = userId,
                TransferGroupId = transferGroupId,
                Kind = TransactionKind.LoanPrincipal
            };
            payingAccount.Balance -= principalPortion;

            var principalIncomeLeg = new Transaction
            {
                Description = $"Principal payment from {payingAccount.Name}",
                Amount = principalPortion,
                Date = dto.Date,
                Type = TransactionType.Income,
                AccountId = dto.LoanAccountId,
                UserId = userId,
                TransferGroupId = transferGroupId,
                Kind = TransactionKind.LoanPrincipal
            };
            // Balance is negative (debt); an "Income" leg here correctly
            // moves it toward zero — exactly how a credit card payment
            // already works via CreateTransferAsync.
            loanAccount.Balance += principalPortion;

            _context.Transactions.Add(principalExpenseLeg);
            _context.Transactions.Add(principalIncomeLeg);
        }

        // Advance the next due date by one month — a simple, reasonable
        // default. Doesn't attempt to reconcile against a specific
        // scheduled date if payments happen off-cycle; that's a refinement
        // for later, not required for this to be correct today.
        if (loanAccount.LoanDetails.NextEmiDueDate.HasValue)
        {
            loanAccount.LoanDetails.NextEmiDueDate = loanAccount.LoanDetails.NextEmiDueDate.Value.AddMonths(1);
        }

        _context.Accounts.Update(payingAccount);
        _context.Accounts.Update(loanAccount);

        var saveResult = await ConcurrencySafeSave.TrySaveChangesAsync(_context);
        if (saveResult.IsFailure)
            return Result.Failure<LoanPaymentResultDto>(saveResult.Error);

        await _cache.InvalidateDashboardAsync(userId);

        return Result.Success(new LoanPaymentResultDto
        {
            InterestPortion = interestPortion,
            PrincipalPortion = principalPortion,
            RemainingBalance = outstandingBalance - principalPortion
        });
    }

    public async Task<Result<DateTime>> AdvanceLoanDueDateAsync(string userId, int loanAccountId, int months)
    {
        if (months < 1 || months > 24)
            return Result.Failure<DateTime>(new Error("Loan.InvalidMonths", "Choose between 1 and 24 months."));

        var loanAccount = await _context.Accounts
            .Include(a => a.AccountCategory)
            .Include(a => a.LoanDetails)
            .FirstOrDefaultAsync(a => a.Id == loanAccountId);

        if (loanAccount == null || loanAccount.UserId != userId)
            return Result.Failure<DateTime>(new Error("Account.NotFound", "Loan account not found."));

        if (loanAccount.AccountCategory?.AccountType != AccountType.Loan || loanAccount.LoanDetails?.NextEmiDueDate == null)
            return Result.Failure<DateTime>(new Error("Loan.NoDueDate", "This loan has no next EMI due date to move."));

        var next = loanAccount.LoanDetails.NextEmiDueDate.Value.AddMonths(months);
        loanAccount.LoanDetails.NextEmiDueDate = next;

        await _context.SaveChangesAsync(default);
        // LoanDetails has no UserId, so the SaveChanges hook can't see this change. Free in
        // memory-only mode (default), so it is always safe to call.
        await _cache.InvalidateDashboardAsync(userId);

        return Result.Success(next);
    }

    public async Task<Result<AmortizationScheduleDto>> GetAmortizationScheduleAsync(string userId, int loanAccountId)
    {
        var loanAccount = await _context.Accounts
            .Include(a => a.AccountCategory)
            .Include(a => a.LoanDetails)
            .FirstOrDefaultAsync(a => a.Id == loanAccountId);

        if (loanAccount == null || loanAccount.UserId != userId)
            return Result.Failure<AmortizationScheduleDto>(new Error("Account.NotFound", "Loan account not found."));

        if (loanAccount.AccountCategory?.AccountType != AccountType.Loan || loanAccount.LoanDetails == null)
            return Result.Failure<AmortizationScheduleDto>(new Error("LoanPayment.NotALoan", "This account isn't set up as a loan."));

        var details = loanAccount.LoanDetails;
        if (!details.EmiAmount.HasValue || !details.InterestRate.HasValue)
        {
            return Result.Failure<AmortizationScheduleDto>(new Error(
                "LoanPayment.IncompleteDetails",
                "EMI amount and interest rate both need to be set on this loan before a schedule can be projected."));
        }

        // Projects FORWARD from the loan's REAL current outstanding
        // balance, not the original PrincipalAmount — once any real
        // payments have been made via MakeLoanPaymentAsync, the original
        // principal no longer reflects where the loan actually stands.
        var currentOutstanding = Math.Abs(loanAccount.Balance);

        var schedule = LoanAmortization.GenerateSchedule(
            currentOutstanding,
            details.InterestRate.Value,
            details.EmiAmount.Value,
            maxMonths: details.TenureMonths ?? 360, // generous fallback cap if tenure isn't set
            startDate: DateTime.UtcNow);

        return Result.Success(new AmortizationScheduleDto
        {
            Schedule = schedule,
            CurrentOutstandingBalance = currentOutstanding,
            EstimatedMonthsRemaining = schedule.Count,
            TotalInterestRemaining = schedule.Sum(s => s.InterestComponent)
        });
    }
}
