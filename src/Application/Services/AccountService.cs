using Application.Common.Helpers;
using Application.Common.Models;
using Application.Contracts;
using Application.DTOs.Accounts;
using Domain.Entities;
using Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Application.Services;

public class AccountService : IAccountService
{
    private readonly IApplicationDbContext _context;
    private readonly ICacheService _cache;

    public AccountService(IApplicationDbContext context, ICacheService cache)
    {
        _context = context;
        _cache = cache;
    }

    public async Task<Result<PaginatedResult<AccountDto>>> GetPagedAccountsAsync(string userId, QueryParameters queryParams)
    {
        var queryable = _context.Accounts
            .Where(a => a.UserId == userId && !a.IsDeleted)
            .Include(a => a.AccountCategory)
            .Include(a => a.CreditCardDetails)
            .Include(a => a.LoanDetails)
            .Include(a => a.BankAccountDetails)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(queryParams.GlobalSearch))
        {
            var search = queryParams.GlobalSearch.Trim().ToLower();
            queryable = queryable.Where(a =>
                a.Name.ToLower().Contains(search) ||
                (a.AccountCategory != null && a.AccountCategory.Name.ToLower().Contains(search)));
        }

        if (queryParams.AccountCategoryId.HasValue)
        {
            queryable = queryable.Where(a => a.AccountCategoryId == queryParams.AccountCategoryId.Value);
        }

        var totalRecords = await queryable.CountAsync();

        queryable = queryParams.SortBy?.ToLower() switch
        {
            "name" => queryParams.SortOrder == "desc" ? queryable.OrderByDescending(a => a.Name.ToLower()).ThenByDescending(a => a.Id) : queryable.OrderBy(a => a.Name.ToLower()).ThenByDescending(a => a.Id),
            "balance" => queryParams.SortOrder == "desc" ? queryable.OrderByDescending(a => a.Balance).ThenByDescending(a => a.Id) : queryable.OrderBy(a => a.Balance).ThenByDescending(a => a.Id),
            "category" or "accountcategoryname" => queryParams.SortOrder == "desc" ? queryable.OrderByDescending(a => a.AccountCategory != null ? a.AccountCategory.Name.ToLower() : "").ThenByDescending(a => a.Id) : queryable.OrderBy(a => a.AccountCategory != null ? a.AccountCategory.Name.ToLower() : "").ThenByDescending(a => a.Id),
            _ => queryable.OrderBy(a => a.Name.ToLower()).ThenByDescending(a => a.Id)
        };

        var items = await queryable
            .Skip((queryParams.PageNumber - 1) * queryParams.PageSize)
            .Take(queryParams.PageSize)
            .ToListAsync();

        var pagedDto = new PaginatedResult<AccountDto>(
            items.Select(a => MapToDto(a, null)).ToList(),
            totalRecords,
            queryParams.PageNumber,
            queryParams.PageSize
        );
        return Result.Success(pagedDto);
    }

    public async Task<Result<List<AccountDto>>> GetAllAccountsAsync(string userId)
    {
        // FIX: was missing .Include(a => a.AccountCategory) entirely — every
        // account returned by this specific method had AccountCategoryName
        // silently coming back empty, regardless of what category was
        // actually set. Found while extending this method for the detail
        // records; fixed alongside since it's directly adjacent.
        var items = await _context.Accounts
            .Where(a => a.UserId == userId && !a.IsDeleted && !a.IsArchived)
            .Include(a => a.AccountCategory)
            .Include(a => a.CreditCardDetails)
            .Include(a => a.LoanDetails)
            .Include(a => a.BankAccountDetails)
            .OrderBy(a => a.Name)
            .ToListAsync();

        return Result.Success(items.Select(a => MapToDto(a, null)).ToList());
    }

    public async Task<Result<AccountDto>> UpsertAccountAsync(string userId, UpsertAccountDto dto)
    {
        dto.Name = ToTitleCase(dto.Name);
        Account? account = null;
        var normalizedName = dto.Name.Trim().ToLower();

        var exists = await _context.Accounts.AnyAsync(a =>
            a.UserId == userId &&
            a.Name.ToLower() == normalizedName &&
            (!dto.Id.HasValue || a.Id != dto.Id.Value));

        if (exists)
            return Result.Failure<AccountDto>(new Error("Account.Duplicate", $"An account with the name '{dto.Name}' already exists."));

        var category = await _context.AccountCategories.FindAsync(dto.AccountCategoryId);
        if (category == null || category.UserId != userId)
            return Result.Failure<AccountDto>(new Error("AccountCategory.NotFound", "Account category not found."));

        if (dto.Id.HasValue && dto.Id > 0)
        {
            // Edit Mode — load with details included so SyncAccountDetailsAsync
            // can see what's currently attached and decide what to add/update/remove.
            account = await _context.Accounts
                .Include(a => a.AccountCategory)
                .Include(a => a.CreditCardDetails)
                .Include(a => a.LoanDetails)
                .Include(a => a.BankAccountDetails)
                .FirstOrDefaultAsync(a => a.Id == dto.Id);

            if (account == null || account.UserId != userId)
                return Result.Failure<AccountDto>(new Error("Account.NotFound", "Account not found."));

            account.Name = dto.Name;
            // Balance is deliberately NOT touched here — it can only be
            // set at creation (the else-branch below) or corrected via
            // AdjustBalanceAsync, which creates a real, visible
            // transaction explaining the change. Silently accepting
            // dto.Balance on every edit would mean a direct API call
            // (bypassing whatever the frontend currently disables) could
            // still overwrite it with zero record of why.
            account.AccountCategoryId = dto.AccountCategoryId;
            account.Purpose = dto.Purpose;
            _context.Accounts.Update(account);
        }
        else
        {
            // Create Mode
            account = new Account
            {
                Name = dto.Name,
                Balance = dto.Balance,
                AccountCategoryId = dto.AccountCategoryId,
                UserId = userId,
                Purpose = dto.Purpose
            };
            _context.Accounts.Add(account);
            await _context.SaveChangesAsync(); // need account.Id before creating a detail record with an FK to it

            // Opening Balance transaction — makes the starting balance
            // auditable through transaction history instead of a number
            // that just appears with no record of where it came from.
            // This is also the actual fix for "Brought Forward" being
            // wrong: that calculation sums transaction history before a
            // period start, and now the starting balance is part of that
            // history instead of invisible to it. Skipped for a genuine
            // ₹0 starting balance — nothing meaningful to record.
            if (dto.Balance != 0)
            {
                _context.Transactions.Add(new Transaction
                {
                    Description = "Opening Balance",
                    Amount = Math.Abs(dto.Balance),
                    Date = DateTime.UtcNow,
                    Type = dto.Balance > 0 ? TransactionType.Income : TransactionType.Expense,
                    AccountId = account.Id,
                    UserId = userId,
                    IsOpeningBalance = true,
                    Kind = TransactionKind.OpeningBalance,
                    // Reuses the same TransferGroupId exclusion trick as
                    // Balance Adjustment — a singleton, unpaired GUID.
                    // Every place that already filters TransferGroupId
                    // == null (Monthly Income/Expense, Deep Insights)
                    // automatically excludes this too, for free.
                    // "Brought Forward" deliberately has NO
                    // TransferGroupId filter in its own query, so it
                    // correctly still counts this — that's the whole fix.
                    TransferGroupId = Guid.NewGuid()
                });
            }
        }

        await SyncAccountDetailsAsync(account, category.AccountType, dto);
        await _context.SaveChangesAsync();

        var resultDto = MapToDto(account, category);
        await _cache.RemoveByPrefixAsync($"dash::{userId}:"); // Invalidate net worth
        return Result.Success(resultDto);
    }

    /// <summary>
    /// Creates/updates the ONE detail record matching categoryType, and
    /// removes the other two if they exist — handles both "user filled in
    /// details for the first time" and "user recategorized this account to
    /// a different AccountType, so the old detail record is now stale."
    /// </summary>
    private async Task SyncAccountDetailsAsync(Account account, AccountType categoryType, UpsertAccountDto dto)
    {
        // Credit Card
        if (categoryType == AccountType.CreditCard && dto.CreditCardDetails != null)
        {
            if (account.CreditCardDetails == null)
            {
                account.CreditCardDetails = new CreditCardDetails { AccountId = account.Id };
                _context.CreditCardDetails.Add(account.CreditCardDetails);
            }
            account.CreditCardDetails.CreditLimit = dto.CreditCardDetails.CreditLimit;
            account.CreditCardDetails.MinimumDueAmount = dto.CreditCardDetails.MinimumDueAmount;
            account.CreditCardDetails.DueDate = dto.CreditCardDetails.DueDate;
            account.CreditCardDetails.StatementClosingDate = dto.CreditCardDetails.StatementClosingDate;
            account.CreditCardDetails.AnnualFee = dto.CreditCardDetails.AnnualFee;
            account.CreditCardDetails.InterestRate = dto.CreditCardDetails.InterestRate;
        }
        else if (account.CreditCardDetails != null)
        {
            _context.CreditCardDetails.Remove(account.CreditCardDetails);
        }

        // Loan
        if (categoryType == AccountType.Loan && dto.LoanDetails != null)
        {
            if (account.LoanDetails == null)
            {
                account.LoanDetails = new LoanDetails { AccountId = account.Id };
                _context.LoanDetails.Add(account.LoanDetails);
            }
            account.LoanDetails.PrincipalAmount = dto.LoanDetails.PrincipalAmount;
            account.LoanDetails.InterestRate = dto.LoanDetails.InterestRate;
            account.LoanDetails.EmiAmount = dto.LoanDetails.EmiAmount;
            account.LoanDetails.TenureMonths = dto.LoanDetails.TenureMonths;
            account.LoanDetails.NextEmiDueDate = dto.LoanDetails.NextEmiDueDate;
            account.LoanDetails.StartDate = dto.LoanDetails.StartDate;
            account.LoanDetails.DesignatedPayingAccountId = dto.LoanDetails.DesignatedPayingAccountId;
        }
        else if (account.LoanDetails != null)
        {
            _context.LoanDetails.Remove(account.LoanDetails);
        }

        // Bank
        if (categoryType == AccountType.Bank && dto.BankAccountDetails != null)
        {
            if (account.BankAccountDetails == null)
            {
                account.BankAccountDetails = new BankAccountDetails { AccountId = account.Id };
                _context.BankAccountDetails.Add(account.BankAccountDetails);
            }
            account.BankAccountDetails.InterestRate = dto.BankAccountDetails.InterestRate;
            account.BankAccountDetails.InterestFrequency = dto.BankAccountDetails.InterestFrequency;
            account.BankAccountDetails.MinimumBalance = dto.BankAccountDetails.MinimumBalance;
        }
        else if (account.BankAccountDetails != null)
        {
            _context.BankAccountDetails.Remove(account.BankAccountDetails);
        }
    }

    public async Task<Result<bool>> DeleteAccountAsync(string userId, int accountId)
    {
        var account = await _context.Accounts.FindAsync(accountId);
        if (account == null || account.UserId != userId)
            return Result.Failure<bool>(new Error("Account.NotFound", "Account not found."));

        account.IsDeleted = true;
        account.DeletedAt = DateTime.UtcNow;
        _context.Accounts.Update(account);
        await _context.SaveChangesAsync();

        await _cache.RemoveByPrefixAsync($"dash::{userId}:"); // Invalidate net worth
        return Result.Success(true);
    }

    public async Task<Result<bool>> MergeAccountsAsync(string userId, MergeAccountsDto dto)
    {
        if (dto.SourceAccountId == dto.TargetAccountId)
            return Result.Failure<bool>(new Error("Account.SameAccount", "Cannot merge an account into itself."));

        var source = await _context.Accounts.FindAsync(dto.SourceAccountId);
        var target = await _context.Accounts.FindAsync(dto.TargetAccountId);

        if (source == null || source.UserId != userId || target == null || target.UserId != userId)
            return Result.Failure<bool>(new Error("Account.NotFound", "One or both accounts could not be found."));

        // Move every transaction from source to target — this is the
        // actual merge. Nothing about individual transactions changes
        // except which account they belong to.
        var affectedTransactions = await _context.Transactions
            .Where(t => t.AccountId == dto.SourceAccountId)
            .ToListAsync();

        foreach (var transaction in affectedTransactions)
        {
            transaction.AccountId = dto.TargetAccountId;
        }
        _context.Transactions.UpdateRange(affectedTransactions);

        // Sets the balance directly rather than deriving it — deliberately
        // NOT subject to the negative-balance guard from earlier. A merge
        // is a data-correction/consolidation action with the user seeing
        // both original balances before confirming, same reasoning as why
        // DeleteTransactionAsync is also exempt from that guard: forcing
        // extra hoops on a correction the user explicitly chose isn't
        // protection, it's friction.
        target.Balance = dto.FinalBalance;
        _context.Accounts.Update(target);

        // Retire the source account
        source.IsDeleted = true;
        source.DeletedAt = DateTime.UtcNow;
        _context.Accounts.Update(source);

        await _context.SaveChangesAsync();
        await _cache.RemoveByPrefixAsync($"dash::{userId}:");

        return Result.Success(true);
    }

    public async Task<Result<bool>> SetArchivedStatusAsync(string userId, int accountId, bool isArchived)
    {
        var account = await _context.Accounts.FindAsync(accountId);
        if (account == null || account.UserId != userId)
            return Result.Failure<bool>(new Error("Account.NotFound", "Account not found."));

        account.IsArchived = isArchived;
        _context.Accounts.Update(account);
        await _context.SaveChangesAsync();
        await _cache.RemoveByPrefixAsync($"dash::{userId}:");

        return Result.Success(true);
    }

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

        await _cache.RemoveByPrefixAsync($"dash::{userId}:");

        return Result.Success(new LoanPaymentResultDto
        {
            InterestPortion = interestPortion,
            PrincipalPortion = principalPortion,
            RemainingBalance = outstandingBalance - principalPortion
        });
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

    public async Task<Result<CreditCardBreakdownDto>> GetCreditCardBreakdownAsync(string userId, int accountId)
    {
        var account = await _context.Accounts
            .Include(a => a.AccountCategory)
            .Include(a => a.CreditCardDetails)
            .FirstOrDefaultAsync(a => a.Id == accountId);

        if (account == null || account.UserId != userId)
            return Result.Failure<CreditCardBreakdownDto>(new Error("Account.NotFound", "Account not found."));

        if (account.AccountCategory?.AccountType != AccountType.CreditCard || account.CreditCardDetails == null)
            return Result.Failure<CreditCardBreakdownDto>(new Error("CreditCard.NotACard", "This account isn't set up as a credit card."));

        var breakdown = await CreditCardStatementCalculator.CalculateAsync(_context, account);

        var latestBill = await _context.CreditCardBills
            .Where(b => b.AccountId == accountId)
            .OrderByDescending(b => b.StatementDate)
            .FirstOrDefaultAsync();

        decimal totalPaidForCycle = 0;
        if (latestBill != null)
        {
            totalPaidForCycle = await _context.CreditCardPayments
                .Where(p => p.CreditCardAccountId == accountId &&
                            (p.CreditCardBillId == latestBill.Id || (p.CreditCardBillId == null && p.Date >= latestBill.StatementDate)))
                .SumAsync(p => (decimal?)p.Amount) ?? 0;
        }

        var minDue = latestBill?.MinimumDue ?? account.CreditCardDetails.MinimumDueAmount;
        var billAmount = latestBill?.BillAmount;
        var remainingBillAmount = billAmount.HasValue ? Math.Max(0, billAmount.Value - totalPaidForCycle) : ((breakdown.StatementOutstanding > 0) ? Math.Max(0, breakdown.StatementOutstanding - totalPaidForCycle) : 0);
        var isBillFullyPaid = latestBill != null && ((billAmount.HasValue && billAmount.Value > 0) ? (totalPaidForCycle >= billAmount.Value) : (latestBill.IsPaid && remainingBillAmount <= 0));
        var isMinDuePaid = isBillFullyPaid || ((minDue.HasValue && minDue.Value > 0) ? (totalPaidForCycle >= minDue.Value) : (totalPaidForCycle > 0));

        return Result.Success(new CreditCardBreakdownDto
        {
            TotalOutstanding = breakdown.TotalOutstanding,
            StatementOutstanding = breakdown.StatementOutstanding,
            UnbilledOutstanding = breakdown.UnbilledOutstanding,
            MostRecentStatementDate = breakdown.MostRecentStatementDate ?? DateTime.UtcNow,
            MinimumDueAmount = minDue,
            DueDate = latestBill?.DueDate ?? account.CreditCardDetails.DueDate,
            LatestBillAmount = latestBill?.BillAmount,
            LatestBillIsPaid = isBillFullyPaid,
            IsMinimumDuePaid = isMinDuePaid,
            TotalPaidForCycle = totalPaidForCycle,
            RemainingBillAmount = remainingBillAmount
        });
    }

    public async Task<Result<List<OutstandingBillDto>>> GetOutstandingBillsAsync(string userId, int accountId)
    {
        var account = await _context.Accounts
            .FirstOrDefaultAsync(a => a.Id == accountId);

        if (account == null || account.UserId != userId)
            return Result.Failure<List<OutstandingBillDto>>(new Error("Account.NotFound", "Account not found."));

        var bills = await _context.CreditCardBills
            .Where(b => b.AccountId == accountId)
            .OrderBy(b => b.StatementDate)
            .ToListAsync();

        var result = new List<OutstandingBillDto>();

        foreach (var b in bills)
        {
            // Same computation as the payment method itself uses to
            // resolve targetBill — paid amount is summed fresh from
            // linked payments, not read from a stored running total,
            // so this can't fall out of sync with what a real payment
            // would actually be validated against.
            var paid = await _context.CreditCardPayments
                .Where(p => p.CreditCardAccountId == accountId && !p.IsDeleted &&
                            (p.CreditCardBillId == b.Id || (p.CreditCardBillId == null && p.Date >= b.StatementDate)))
                .SumAsync(p => (decimal?)p.Amount) ?? 0;

            if (paid < b.BillAmount)
            {
                result.Add(new OutstandingBillDto
                {
                    Id = b.Id,
                    StatementDate = b.StatementDate,
                    BillAmount = b.BillAmount,
                    PaidAmount = paid,
                    RemainingAmount = b.BillAmount - paid,
                    MinimumDue = b.MinimumDue,
                    DueDate = b.DueDate
                });
            }
        }

        return Result.Success(result);
    }

    public async Task<Result<AccountDto>> AdjustBalanceAsync(string userId, AdjustBalanceDto dto)
    {
        var account = await _context.Accounts
            .Include(a => a.AccountCategory)
            .FirstOrDefaultAsync(a => a.Id == dto.AccountId);

        if (account == null || account.UserId != userId)
            return Result.Failure<AccountDto>(new Error("Account.NotFound", "Account not found."));

        var delta = dto.NewBalance - account.Balance;

        if (delta == 0)
            return Result.Failure<AccountDto>(new Error("Adjustment.NoChange", "The new balance matches the current balance — nothing to adjust."));

        var reasonText = string.IsNullOrWhiteSpace(dto.Reason)
            ? "Balance Adjustment"
            : $"Balance Adjustment — {dto.Reason.Trim()}";

        var adjustmentTransaction = new Transaction
        {
            Description = reasonText,
            Amount = Math.Abs(delta),
            Date = DateTime.UtcNow,
            Type = delta > 0 ? TransactionType.Income : TransactionType.Expense,
            AccountId = dto.AccountId,
            UserId = userId,
            IsBalanceAdjustment = true,
            Kind = TransactionKind.BalanceAdjustment,

            // Reuses the existing transfer-exclusion mechanism rather
            // than adding a second, parallel filter to every one of the
            // ~10 places that already check TransferGroupId == null.
            TransferGroupId = Guid.NewGuid()
        };

        _context.Transactions.Add(adjustmentTransaction);

        // Set directly to the target rather than applying the delta —
        // avoids any possible floating-point drift between the two
        // approaches, and guarantees the result is exactly what was typed.
        account.Balance = dto.NewBalance;
        _context.Accounts.Update(account);

        var saveResult = await ConcurrencySafeSave.TrySaveChangesAsync(_context);
        if (saveResult.IsFailure)
            return Result.Failure<AccountDto>(saveResult.Error);

        await _cache.RemoveByPrefixAsync($"dash::{userId}:");

        return Result.Success(MapToDto(account, account.AccountCategory));
    }

    public async Task<Result<CreditCardBillResultDto>> RecordCreditCardBillAsync(string userId, RecordCreditCardBillDto dto)
    {
        var account = await _context.Accounts
            .Include(a => a.AccountCategory)
            .Include(a => a.CreditCardDetails)
            .FirstOrDefaultAsync(a => a.Id == dto.AccountId);

        if (account == null || account.UserId != userId)
            return Result.Failure<CreditCardBillResultDto>(new Error("Account.NotFound", "Account not found."));

        if (account.AccountCategory?.AccountType != AccountType.CreditCard || account.CreditCardDetails == null)
            return Result.Failure<CreditCardBillResultDto>(new Error("CreditCard.NotACard", "This account isn't set up as a credit card."));

        if (dto.BillAmount < 0)
            return Result.Failure<CreditCardBillResultDto>(new Error("CreditCardBill.InvalidAmount", "Bill amount can't be negative."));

        var computedBreakdown = await CreditCardStatementCalculator.CalculateAsync(_context, account);
        var statementDate = computedBreakdown.MostRecentStatementDate ?? DateTime.UtcNow;

        // Upsert against (AccountId, StatementDate) — recording a bill
        // for a cycle that already has one updates it rather than
        // creating a duplicate, so re-entering a corrected figure is safe.
        var existingBill = await _context.CreditCardBills
            .FirstOrDefaultAsync(b => b.AccountId == dto.AccountId &&
                                      b.StatementDate.Date == statementDate.Date);

        if (existingBill != null)
        {
            existingBill.BillAmount = dto.BillAmount;
            existingBill.MinimumDue = dto.MinimumDue;
            existingBill.DueDate = dto.DueDate ?? account.CreditCardDetails.DueDate;
            _context.CreditCardBills.Update(existingBill);
        }
        else
        {
            _context.CreditCardBills.Add(new CreditCardBill
            {
                UserId = userId,
                AccountId = dto.AccountId,
                StatementDate = statementDate,
                BillAmount = dto.BillAmount,
                MinimumDue = dto.MinimumDue,
                DueDate = dto.DueDate ?? account.CreditCardDetails.DueDate
            });
        }

        if (account.CreditCardDetails != null)
        {
            if (dto.MinimumDue.HasValue)
            {
                account.CreditCardDetails.MinimumDueAmount = dto.MinimumDue.Value;
            }
            if (dto.DueDate.HasValue)
            {
                account.CreditCardDetails.DueDate = dto.DueDate.Value;
            }
            _context.CreditCardDetails.Update(account.CreditCardDetails);
        }

        await _context.SaveChangesAsync();
        await _cache.RemoveByPrefixAsync($"dash::{userId}:");

        var impliedCharges = Math.Max(0, dto.BillAmount - computedBreakdown.StatementOutstanding);

        return Result.Success(new CreditCardBillResultDto
        {
            RecordedBillAmount = dto.BillAmount,
            ComputedFromTransactions = computedBreakdown.StatementOutstanding,
            ImpliedInterestAndFees = impliedCharges,
            StatementDate = statementDate
        });
    }

    public async Task<Result<CreditCardPaymentBatchResultDto>> MakeCreditCardPaymentBatchAsync(
        string userId, MakeCreditCardPaymentBatchDto dto)
    {
        if (dto.Payments == null || dto.Payments.Count == 0)
            return Result.Failure<CreditCardPaymentBatchResultDto>(new Error(
                "CreditCardPayment.NoPayments", "At least one payment is required."));

        var ccAccount = await _context.Accounts
            .Include(a => a.AccountCategory)
            .Include(a => a.CreditCardDetails)
            .FirstOrDefaultAsync(a => a.Id == dto.CreditCardAccountId);

        if (ccAccount == null || ccAccount.UserId != userId)
            return Result.Failure<CreditCardPaymentBatchResultDto>(new Error("Account.NotFound", "Credit card account not found."));

        if (ccAccount.AccountCategory?.AccountType != AccountType.CreditCard || ccAccount.CreditCardDetails == null)
            return Result.Failure<CreditCardPaymentBatchResultDto>(new Error(
                "CreditCardPayment.NotACard", "This account isn't set up as a credit card."));

        var computedBreakdown = await CreditCardStatementCalculator.CalculateAsync(_context, ccAccount);

        CreditCardBill? targetBill = null;
        if (dto.CreditCardBillId.HasValue)
        {
            targetBill = await _context.CreditCardBills
                .FirstOrDefaultAsync(b => b.Id == dto.CreditCardBillId.Value && b.AccountId == dto.CreditCardAccountId && b.UserId == userId);
        }

        if (targetBill == null)
        {
            var bills = await _context.CreditCardBills
                .Where(b => b.AccountId == dto.CreditCardAccountId)
                .OrderByDescending(b => b.StatementDate)
                .ToListAsync();

            foreach (var b in bills)
            {
                var paid = await _context.CreditCardPayments
                    .Where(p => p.CreditCardAccountId == dto.CreditCardAccountId && !p.IsDeleted &&
                                (p.CreditCardBillId == b.Id || (p.CreditCardBillId == null && p.Date >= b.StatementDate)))
                    .SumAsync(p => (decimal?)p.Amount) ?? 0;

                if (paid < b.BillAmount)
                {
                    targetBill = b;
                    break;
                }
            }

            if (targetBill == null && bills.Count > 0)
            {
                targetBill = bills[0];
            }
        }

        decimal remainingBillPayable = 0;
        if (targetBill != null)
        {
            var paidForBill = await _context.CreditCardPayments
                .Where(p => p.CreditCardAccountId == dto.CreditCardAccountId && !p.IsDeleted &&
                            (p.CreditCardBillId == targetBill.Id || (p.CreditCardBillId == null && p.Date >= targetBill.StatementDate)))
                .SumAsync(p => (decimal?)p.Amount) ?? 0;

            remainingBillPayable = Math.Max(0, targetBill.BillAmount - paidForBill);
        }

        decimal maxPayable = Math.Max(Math.Abs(ccAccount.Balance), computedBreakdown.TotalOutstanding);
        if (remainingBillPayable > 0)
        {
            maxPayable = Math.Max(maxPayable, remainingBillPayable);
        }

        if (maxPayable == 0 && ccAccount.CreditCardDetails?.CreditLimit.HasValue == true && ccAccount.Balance < 0)
        {
            maxPayable = Math.Abs(ccAccount.Balance);
        }

        if (maxPayable <= 0)
        {
            return Result.Failure<CreditCardPaymentBatchResultDto>(new Error(
                "CreditCardPayment.NoOutstanding", "There is no outstanding balance or bill to pay on this card."));
        }

        var totalRequested = dto.Payments.Sum(p => p.Amount);

        if (totalRequested > maxPayable + 0.01m)
            return Result.Failure<CreditCardPaymentBatchResultDto>(new Error(
                "CreditCardPayment.ExceedsPayoff",
                $"These payments total ₹{totalRequested:F2}, which exceeds the max owed amount of ₹{maxPayable:F2} on this card."));

        var payingAccounts = new Dictionary<int, Account>();

        // Cumulative wallet usage across this whole batch, per app —
        // needed since more than one payment row could draw from the
        // same app's balance in a single request, and each needs
        // validating against what's actually left after the others.
        var walletUsageThisBatch = new Dictionary<string, decimal>();

        foreach (var payment in dto.Payments)
        {
            if (payment.Amount <= 0)
                return Result.Failure<CreditCardPaymentBatchResultDto>(new Error(
                    "CreditCardPayment.InvalidAmount", "Every payment amount must be greater than zero."));

            if (payment.PayingAccountId == dto.CreditCardAccountId)
                return Result.Failure<CreditCardPaymentBatchResultDto>(new Error(
                    "CreditCardPayment.SameAccount", "The credit card and the paying account can't be the same account."));

            if (!payingAccounts.TryGetValue(payment.PayingAccountId, out var payingAccount))
            {
                payingAccount = await _context.Accounts
                    .Include(a => a.AccountCategory)
                    .FirstOrDefaultAsync(a => a.Id == payment.PayingAccountId);

                if (payingAccount == null || payingAccount.UserId != userId)
                    return Result.Failure<CreditCardPaymentBatchResultDto>(new Error(
                        "Account.NotFound", $"Paying account {payment.PayingAccountId} not found."));

                if (payingAccount.AccountCategory?.AccountType == AccountType.CreditCard ||
                    payingAccount.AccountCategory?.AccountType == AccountType.Loan ||
                    (payingAccount.AccountCategory?.IsLiability ?? false))
                {
                    return Result.Failure<CreditCardPaymentBatchResultDto>(new Error(
                        "CreditCardPayment.InvalidPayingAccount",
                        $"{payingAccount.Name} is a liability account and cannot be used as a payment source."));
                }

                payingAccounts[payment.PayingAccountId] = payingAccount;
            }

            var actualCashNeeded = payment.Amount - payment.AppliedWalletAmount;
            if (payingAccount.Balance < actualCashNeeded)
                return Result.Failure<CreditCardPaymentBatchResultDto>(new Error(
                    "CreditCardPayment.InsufficientFunds",
                    $"{payingAccount.Name} has insufficient balance for a ₹{actualCashNeeded:F2} payment. Available: ₹{payingAccount.Balance:F2}"));

            if (payment.CashbackAmount.HasValue && payment.CashbackAmount.Value > 0)
            {
                if (payment.CashbackAmount.Value > payment.Amount)
                    return Result.Failure<CreditCardPaymentBatchResultDto>(new Error(
                        "CreditCardPayment.CashbackExceedsPayment",
                        "Cashback can't be more than the payment amount itself."));

                if (payment.CashbackType == CashbackType.Direct && !payment.CashbackAccountId.HasValue)
                    return Result.Failure<CreditCardPaymentBatchResultDto>(new Error(
                        "CreditCardPayment.MissingCashbackAccount",
                        "Direct cashback needs an account to credit it to."));

                if (payment.CashbackAccountId.HasValue && payment.CashbackAccountId.Value == dto.CreditCardAccountId)
                    return Result.Failure<CreditCardPaymentBatchResultDto>(new Error(
                        "CreditCardPayment.CashbackToSameCard",
                        "Cashback can't be credited back to the same card being paid."));
            }

            if (payment.AppliedWalletAmount > 0)
            {
                if (string.IsNullOrWhiteSpace(payment.PaymentAppName))
                    return Result.Failure<CreditCardPaymentBatchResultDto>(new Error(
                        "CreditCardPayment.MissingAppForWalletDiscount",
                        "Applying a wallet discount requires specifying which payment app it's from."));

                if (payment.AppliedWalletAmount > payment.Amount)
                    return Result.Failure<CreditCardPaymentBatchResultDto>(new Error(
                        "CreditCardPayment.WalletDiscountExceedsPayment",
                        "The applied wallet amount can't be more than the payment itself."));

                var availableBalance = await _context.PaymentAppWalletLedgerEntries
                    .Where(e => e.UserId == userId && e.PaymentAppName == payment.PaymentAppName)
                    .SumAsync(e => e.Type == PaymentAppWalletEntryType.Earned ? e.Amount : -e.Amount);

                walletUsageThisBatch.TryGetValue(payment.PaymentAppName!, out var alreadyUsedInBatch);
                var remainingAfterBatch = availableBalance - alreadyUsedInBatch;

                if (payment.AppliedWalletAmount > remainingAfterBatch)
                    return Result.Failure<CreditCardPaymentBatchResultDto>(new Error(
                        "CreditCardPayment.InsufficientWalletBalance",
                        $"Only ₹{remainingAfterBatch:F2} available in {payment.PaymentAppName}'s balance."));

                walletUsageThisBatch[payment.PaymentAppName!] = alreadyUsedInBatch + payment.AppliedWalletAmount;
            }
        }

        decimal remainingInterestToAllocate = 0;
        if (targetBill != null)
        {
            var interestAlreadyPaid = await _context.CreditCardPayments
                .Where(p => p.CreditCardAccountId == dto.CreditCardAccountId && !p.IsDeleted &&
                            (p.CreditCardBillId == targetBill.Id || (p.CreditCardBillId == null && p.Date >= targetBill.StatementDate)))
                .SumAsync(p => (decimal?)p.InterestPortion) ?? 0;

            var totalBillInterest = Math.Max(0, targetBill.BillAmount - computedBreakdown.StatementOutstanding);
            remainingInterestToAllocate = Math.Max(0, totalBillInterest - interestAlreadyPaid);
        }

        var defaultTransferCat = await _context.TransactionCategories.FirstOrDefaultAsync(c => c.UserId == userId && c.Name == "Transfer");
        var defaultInterestCat = await _context.TransactionCategories.FirstOrDefaultAsync(c => c.UserId == userId && (c.Name == "Interest" || c.Name == "Bank Fees" || c.Name == "Interest & Fees" || c.Name == "Interest & Charges"));
        var defaultCashbackCat = await _context.TransactionCategories.FirstOrDefaultAsync(c => c.UserId == userId && (c.Name == "Cashback" || c.Name == "Rewards" || c.Name == "Income" || c.Name == "Cashback & Rewards"));

        var linkTracking = new List<(CreditCardPayment Payment, Transaction? Interest, Transaction? ExpenseLeg, Transaction? IncomeLeg, Transaction? Cashback)>();
        var pendingWalletEntries = new List<(PaymentAppWalletLedgerEntry Entry, CreditCardPayment Payment)>();
        var results = new List<SinglePaymentResultDto>();
        decimal totalPaid = 0, totalInterestPaid = 0, totalCashback = 0, directCashback = 0, indirectCashback = 0;

        foreach (var payment in dto.Payments)
        {
            var payingAccount = payingAccounts[payment.PayingAccountId];

            var interestPortion = Math.Min(remainingInterestToAllocate, payment.Amount);
            var principalPortion = payment.Amount - interestPortion;
            remainingInterestToAllocate -= interestPortion;

            var billIdForPayment = payment.CreditCardBillId ?? targetBill?.Id;

            var ccPayment = new CreditCardPayment
            {
                UserId = userId,
                CreditCardAccountId = dto.CreditCardAccountId,
                PayingAccountId = payment.PayingAccountId,
                CreditCardBillId = billIdForPayment,
                Amount = payment.Amount,
                Date = payment.Date,
                PaymentAppName = payment.PaymentAppName,
                CashbackAmount = payment.CashbackAmount,
                CashbackType = payment.CashbackType,
                CashbackAccountId = payment.CashbackAccountId,
                InterestPortion = interestPortion,
                PrincipalPortion = principalPortion
            };

            Transaction? interestTx = null, expenseLeg = null, incomeLeg = null, cashbackTx = null;

            // 1. Charge event: Log the interest/fee directly on the Credit Card liability
            if (interestPortion > 0)
            {
                interestTx = new Transaction
                {
                    Description = $"Interest/fees — {ccAccount.Name}",
                    Amount = interestPortion,
                    Date = payment.Date,
                    Type = TransactionType.Expense,
                    AccountId = dto.CreditCardAccountId,
                    UserId = userId,
                    Kind = TransactionKind.Normal,
                    TransactionCategoryId = payment.InterestCategoryId ?? defaultInterestCat?.Id
                };
                _context.Transactions.Add(interestTx);
                ccAccount.Balance -= interestPortion;
            }

            // 2. Settlement event: Standard transfer pair (Bank Expense, CC Income)
            if (payment.Amount > 0)
            {
                var transferGroupId = Guid.NewGuid();
                var actualCashFromPayingAccount = payment.Amount - payment.AppliedWalletAmount;

                expenseLeg = new Transaction
                {
                    Description = $"Credit card payment — {ccAccount.Name}" +
                        (payment.PaymentAppName != null ? $" via {payment.PaymentAppName}" : "") +
                        (payment.AppliedWalletAmount > 0 ? $" (₹{payment.AppliedWalletAmount:F2} from wallet balance)" : ""),
                    Amount = actualCashFromPayingAccount,
                    Date = payment.Date,
                    Type = TransactionType.Expense,
                    AccountId = payment.PayingAccountId,
                    UserId = userId,
                    Kind = TransactionKind.Transfer,
                    TransferGroupId = transferGroupId,
                    TransactionCategoryId = defaultTransferCat?.Id
                };
                payingAccount.Balance -= actualCashFromPayingAccount;

                incomeLeg = new Transaction
                {
                    Description = $"Payment from {payingAccount.Name}",
                    Amount = payment.Amount,
                    Date = payment.Date,
                    Type = TransactionType.Income,
                    AccountId = dto.CreditCardAccountId,
                    UserId = userId,
                    Kind = TransactionKind.Transfer,
                    TransferGroupId = transferGroupId,
                    TransactionCategoryId = defaultTransferCat?.Id
                };
                ccAccount.Balance += payment.Amount;

                _context.Transactions.Add(expenseLeg);
                _context.Transactions.Add(incomeLeg);
            }

            // 3. Direct Cashback: real income transaction on destination account
            if (payment.CashbackAmount.HasValue && payment.CashbackAmount > 0)
            {
                totalCashback += payment.CashbackAmount.Value;

                if (payment.CashbackType == CashbackType.Direct && payment.CashbackAccountId.HasValue)
                {
                    var cashbackAccount = await _context.Accounts
                        .FirstOrDefaultAsync(a => a.Id == payment.CashbackAccountId.Value && a.UserId == userId);

                    if (cashbackAccount != null)
                    {
                        cashbackTx = new Transaction
                        {
                            Description = $"Cashback — {ccAccount.Name} payment" +
                                (payment.PaymentAppName != null ? $" ({payment.PaymentAppName})" : ""),
                            Amount = payment.CashbackAmount.Value,
                            Date = payment.Date,
                            Type = TransactionType.Income,
                            AccountId = payment.CashbackAccountId.Value,
                            UserId = userId,
                            TransactionCategoryId = defaultCashbackCat?.Id
                        };
                        _context.Transactions.Add(cashbackTx);
                        cashbackAccount.Balance += payment.CashbackAmount.Value;
                        _context.Accounts.Update(cashbackAccount);
                        directCashback += payment.CashbackAmount.Value;
                    }
                }
                else if (payment.CashbackType == CashbackType.StatementCredit)
                {
                    // Applied directly against the card's own balance —
                    // this is a real credit event, not a wallet
                    // earning. An Income transaction on the CC account
                    // moves its (negative) debt balance toward zero,
                    // same convention already used for the principal
                    // payment's own income leg above.
                    cashbackTx = new Transaction
                    {
                        Description = $"Statement credit — {ccAccount.Name}" +
                            (payment.PaymentAppName != null ? $" ({payment.PaymentAppName})" : ""),
                        Amount = payment.CashbackAmount.Value,
                        Date = payment.Date,
                        Type = TransactionType.Income,
                        AccountId = dto.CreditCardAccountId,
                        UserId = userId,
                        TransactionCategoryId = defaultCashbackCat?.Id
                    };
                    _context.Transactions.Add(cashbackTx);
                    ccAccount.Balance += payment.CashbackAmount.Value;
                }
                else
                {
                    indirectCashback += payment.CashbackAmount.Value;

                    if (!string.IsNullOrWhiteSpace(payment.PaymentAppName))
                    {
                        var earnedEntry = new PaymentAppWalletLedgerEntry
                        {
                            UserId = userId,
                            PaymentAppName = payment.PaymentAppName,
                            Amount = payment.CashbackAmount.Value,
                            Type = PaymentAppWalletEntryType.Earned,
                            Date = payment.Date
                        };
                        pendingWalletEntries.Add((earnedEntry, ccPayment));
                    }
                }
            }

            if (payment.AppliedWalletAmount > 0 && !string.IsNullOrWhiteSpace(payment.PaymentAppName))
            {
                var appliedEntry = new PaymentAppWalletLedgerEntry
                {
                    UserId = userId,
                    PaymentAppName = payment.PaymentAppName,
                    Amount = payment.AppliedWalletAmount,
                    Type = PaymentAppWalletEntryType.Applied,
                    Date = payment.Date
                };
                pendingWalletEntries.Add((appliedEntry, ccPayment));
            }

            _context.CreditCardPayments.Add(ccPayment);
            _context.Accounts.Update(payingAccount);
            linkTracking.Add((ccPayment, interestTx, expenseLeg, incomeLeg, cashbackTx));

            results.Add(new SinglePaymentResultDto
            {
                InterestPortion = interestPortion,
                PrincipalPortion = principalPortion,
                CashbackAmount = payment.CashbackAmount,
                CashbackType = payment.CashbackType
            });

            totalPaid += payment.Amount;
            totalInterestPaid += interestPortion;
        }

        _context.Accounts.Update(ccAccount);

        bool billMarkedPaid = false;
        if (targetBill != null)
        {
            var existingPaidForBill = await _context.CreditCardPayments
                .Where(p => p.CreditCardAccountId == dto.CreditCardAccountId && !p.IsDeleted &&
                            (p.CreditCardBillId == targetBill.Id || (p.CreditCardBillId == null && p.Date >= targetBill.StatementDate)))
                .SumAsync(p => (decimal?)p.Amount) ?? 0;

            var newPaidForBill = dto.Payments
                .Where(p => (p.CreditCardBillId ?? targetBill.Id) == targetBill.Id)
                .Sum(p => p.Amount);

            var totalPaidForThisBill = existingPaidForBill + newPaidForBill;

            if (totalPaidForThisBill >= targetBill.BillAmount)
            {
                targetBill.IsPaid = true;
                billMarkedPaid = true;
            }
            else
            {
                targetBill.IsPaid = false;
            }
            _context.CreditCardBills.Update(targetBill);
        }

        var saveResult = await ConcurrencySafeSave.TrySaveChangesAsync(_context);
        if (saveResult.IsFailure)
            return Result.Failure<CreditCardPaymentBatchResultDto>(saveResult.Error);

        foreach (var (entry, payment) in pendingWalletEntries)
        {
            entry.CreditCardPaymentId = payment.Id;
            _context.PaymentAppWalletLedgerEntries.Add(entry);
        }

        foreach (var (linkedPayment, interest, expense, income, cashback) in linkTracking)
        {
            linkedPayment.InterestTransactionId = interest?.Id;
            linkedPayment.PrincipalExpenseTransactionId = expense?.Id;
            linkedPayment.PrincipalIncomeTransactionId = income?.Id;
            linkedPayment.CashbackTransactionId = cashback?.Id;
            _context.CreditCardPayments.Update(linkedPayment);
        }
        await _context.SaveChangesAsync();

        await _cache.RemoveByPrefixAsync($"dash::{userId}:");

        return Result.Success(new CreditCardPaymentBatchResultDto
        {
            Payments = results,
            TotalPaid = totalPaid,
            TotalInterestPaid = totalInterestPaid,
            TotalCashbackReceived = totalCashback,
            DirectCashbackReceived = directCashback,
            IndirectCashbackReceived = indirectCashback,
            RemainingBalance = Math.Max(0, maxPayable - totalPaid),
            BillMarkedPaid = billMarkedPaid
        });
    }

    public async Task<Result<CashbackInsightsDto>> GetCashbackInsightsAsync(
        string userId, int? accountId = null, int? month = null, int? year = null)
    {
        var query = _context.CreditCardPayments
            .Where(p => p.UserId == userId);

        if (accountId.HasValue)
        {
            query = query.Where(p => p.CreditCardAccountId == accountId.Value);
        }

        if (month.HasValue && year.HasValue)
        {
            var monthStart = new DateTime(year.Value, month.Value, 1, 0, 0, 0, DateTimeKind.Utc);
            var monthEnd = monthStart.AddMonths(1).AddTicks(-1);
            query = query.Where(p => p.Date >= monthStart && p.Date <= monthEnd);
        }

        var payments = await query.ToListAsync();

        var byApp = payments
            .Where(p => p.PaymentAppName != null)
            .GroupBy(p => p.PaymentAppName!)
            .Select(g =>
            {
                var totalPaid = g.Sum(p => p.Amount);
                var totalCashback = g.Sum(p => p.CashbackAmount ?? 0);
                return new PaymentAppCashbackDto
                {
                    PaymentAppName = g.Key,
                    TotalPaid = totalPaid,
                    TotalCashback = totalCashback,
                    EffectiveRatePercent = totalPaid > 0 ? Math.Round((totalCashback / totalPaid) * 100, 2) : 0,
                    PaymentCount = g.Count()
                };
            })
            .OrderByDescending(a => a.EffectiveRatePercent)
            .ToList();

        var unclaimedWalletCashback = payments
            .Where(p => p.CashbackType == CashbackType.Indirect)
            .Sum(p => p.CashbackAmount ?? 0);

        return Result.Success(new CashbackInsightsDto
        {
            ByApp = byApp,
            UnclaimedWalletCashback = unclaimedWalletCashback
        });
    }

    public async Task<Result<List<AccountPaymentSuggestionDto>>> GetPaymentSuggestionsAsync(
        string userId, int creditCardAccountId, decimal amount)
    {
        var ccAccount = await _context.Accounts
            .FirstOrDefaultAsync(a => a.Id == creditCardAccountId);

        if (ccAccount == null || ccAccount.UserId != userId)
            return Result.Failure<List<AccountPaymentSuggestionDto>>(new Error(
                "Account.NotFound", "Credit card account not found."));

        var candidateAccounts = await _context.Accounts
            .Include(a => a.AccountCategory)
            .Where(a => a.UserId == userId && a.Id != creditCardAccountId &&
                        a.AccountCategory.AccountType != AccountType.CreditCard)
            .ToListAsync();

        var suggestions = candidateAccounts
            .Select(a => new AccountPaymentSuggestionDto
            {
                AccountId = a.Id,
                AccountName = a.Name,
                Balance = a.Balance,
                HasSufficientBalance = a.Balance >= amount,
                Shortfall = Math.Max(0, amount - a.Balance)
            })
            .OrderByDescending(s => s.Balance)
            .ToList();

        return Result.Success(suggestions);
    }

    public async Task<Result<List<string>>> GetKnownPaymentAppNamesAsync(string userId)
    {
        var names = await _context.CreditCardPayments
            .Where(p => p.UserId == userId && p.PaymentAppName != null)
            .Select(p => p.PaymentAppName!)
            .Distinct()
            .OrderBy(n => n)
            .ToListAsync();

        return Result.Success(names);
    }

    public async Task<Result<List<PaymentAppWalletDto>>> GetPaymentAppWalletsAsync(string userId)
    {
        var entries = await _context.PaymentAppWalletLedgerEntries
            .Where(e => e.UserId == userId)
            .OrderByDescending(e => e.Date)
            .ToListAsync();

        var result = entries
            .GroupBy(e => e.PaymentAppName)
            .Select(g => new PaymentAppWalletDto
            {
                PaymentAppName = g.Key,
                CurrentBalance = g.Sum(e => e.Type == PaymentAppWalletEntryType.Earned ? e.Amount : -e.Amount),
                RecentEntries = g.Take(20).Select(e => new PaymentAppWalletLedgerEntryDto
                {
                    Amount = e.Amount,
                    Type = e.Type.ToString(),
                    Date = e.Date,
                    CreditCardPaymentId = e.CreditCardPaymentId
                }).ToList()
            })
            .OrderByDescending(w => w.CurrentBalance)
            .ToList();

        return Result.Success(result);
    }

    public async Task<Result<int>> BackfillOpeningBalancesAsync(string userId)
    {
        var accounts = await _context.Accounts
            .Where(a => a.UserId == userId)
            .ToListAsync();

        int created = 0;
        foreach (var account in accounts)
        {
            var alreadyHasOne = await _context.Transactions
                .AnyAsync(t => t.AccountId == account.Id && t.IsOpeningBalance);
            if (alreadyHasOne) continue;

            var existingTransactionsNetEffect = await _context.Transactions
                .Where(t => t.AccountId == account.Id)
                .SumAsync(t => t.Type == TransactionType.Income ? t.Amount : -t.Amount);

            var impliedOpeningBalance = account.Balance - existingTransactionsNetEffect;

            if (impliedOpeningBalance == 0) continue;

            // Dated one day before this account's earliest transaction
            // (or its own creation date if it has none at all), so it
            // always correctly sorts as "before everything else" —
            // including in any date-ordered "Brought Forward" query.
            var earliestTransactionDate = await _context.Transactions
                .Where(t => t.AccountId == account.Id)
                .OrderBy(t => t.Date)
                .Select(t => (DateTime?)t.Date)
                .FirstOrDefaultAsync();

            var backfillDate = (earliestTransactionDate ?? account.CreatedAt).AddDays(-1);

            _context.Transactions.Add(new Transaction
            {
                Description = "Opening Balance (backfilled)",
                Amount = Math.Abs(impliedOpeningBalance),
                Date = backfillDate,
                Type = impliedOpeningBalance > 0 ? TransactionType.Income : TransactionType.Expense,
                AccountId = account.Id,
                UserId = userId,
                IsOpeningBalance = true,
                Kind = TransactionKind.OpeningBalance,
                TransferGroupId = Guid.NewGuid()
            });
            created++;
        }

        await _context.SaveChangesAsync();
        await _cache.RemoveByPrefixAsync($"dash::{userId}:");
        return Result.Success(created);
    }

    private AccountDto MapToDto(Account a, Domain.Entities.AccountCategory? categoryOverride = null)
    {
        var category = categoryOverride ?? a.AccountCategory;

        return new AccountDto
        {
            Id = a.Id,
            Name = a.Name,
            Balance = a.Balance,
            AccountCategoryName = category?.Name ?? "",
            AccountCategoryId = a.AccountCategoryId,
            IsArchived = a.IsArchived,
            Purpose = a.Purpose,
            IsLiability = category?.IsLiability ?? false,
            AccountType = category?.AccountType ?? AccountType.Other,
            CreditCardDetails = a.CreditCardDetails is null ? null : new CreditCardDetailsDto
            {
                CreditLimit = a.CreditCardDetails.CreditLimit,
                MinimumDueAmount = a.CreditCardDetails.MinimumDueAmount,
                DueDate = a.CreditCardDetails.DueDate,
                StatementClosingDate = a.CreditCardDetails.StatementClosingDate,
                AnnualFee = a.CreditCardDetails.AnnualFee,
                InterestRate = a.CreditCardDetails.InterestRate
            },
            LoanDetails = a.LoanDetails is null ? null : new LoanDetailsDto
            {
                PrincipalAmount = a.LoanDetails.PrincipalAmount,
                InterestRate = a.LoanDetails.InterestRate,
                EmiAmount = a.LoanDetails.EmiAmount,
                TenureMonths = a.LoanDetails.TenureMonths,
                NextEmiDueDate = a.LoanDetails.NextEmiDueDate,
                StartDate = a.LoanDetails.StartDate,
                DesignatedPayingAccountId = a.LoanDetails.DesignatedPayingAccountId
            },
            BankAccountDetails = a.BankAccountDetails is null ? null : new BankAccountDetailsDto
            {
                InterestRate = a.BankAccountDetails.InterestRate,
                InterestFrequency = a.BankAccountDetails.InterestFrequency,
                MinimumBalance = a.BankAccountDetails.MinimumBalance
            }
        };
    }

    private string ToTitleCase(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return input;
        return System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(input.ToLower());
    }
}