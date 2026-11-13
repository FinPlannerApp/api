using Application.Common.Helpers;
using Application.Common.Models;
using Application.Contracts;
using Application.DTOs.Accounts;
using Domain.Entities;
using Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Application.Services;

/// <summary>Credit-card bills, payments and breakdowns.</summary>
public partial class AccountService
{
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

    public async Task<Result<CardHistoryDto>> GetCreditCardHistoryAsync(string userId, int accountId)
    {
        var account = await _context.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == accountId);
        if (account == null || account.UserId != userId)
            return Result.Failure<CardHistoryDto>(new Error("Account.NotFound", "Account not found."));

        var bills = await _context.CreditCardBills.AsNoTracking()
            .Where(b => b.AccountId == accountId)
            .OrderByDescending(b => b.StatementDate)
            .ToListAsync();
        var payments = await _context.CreditCardPayments.AsNoTracking()
            .Where(p => p.CreditCardAccountId == accountId)
            .OrderByDescending(p => p.Date)
            .ToListAsync();

        var paymentIds = payments.Select(p => p.Id).ToList();
        var accountIds = payments.Select(p => p.PayingAccountId)
            .Concat(payments.Where(p => p.CashbackAccountId.HasValue).Select(p => p.CashbackAccountId!.Value))
            .Distinct().ToList();
        var names = await _context.Accounts.AsNoTracking()
            .Where(a => accountIds.Contains(a.Id))
            .ToDictionaryAsync(a => a.Id, a => a.Name);
        var ledger = await _context.PaymentAppWalletLedgerEntries.AsNoTracking()
            .Where(e => paymentIds.Contains(e.CreditCardPaymentId))
            .ToListAsync();

        CardPaymentHistoryDto Map(CreditCardPayment p) => new()
        {
            Id = p.Id,
            Date = p.Date,
            Amount = p.Amount,
            PayingAccountId = p.PayingAccountId,
            PayingAccountName = names.GetValueOrDefault(p.PayingAccountId, "Unknown account"),
            PaymentAppName = p.PaymentAppName,
            PrincipalPortion = p.PrincipalPortion,
            InterestPortion = p.InterestPortion,
            CashbackAmount = p.CashbackAmount,
            CashbackType = p.CashbackType?.ToString(),
            CashbackAccountName = p.CashbackAccountId.HasValue ? names.GetValueOrDefault(p.CashbackAccountId.Value) : null,
            WalletApplied = ledger.Where(e => e.CreditCardPaymentId == p.Id && e.Type == PaymentAppWalletEntryType.Applied).Sum(e => e.Amount),
            WalletEarned = ledger.Where(e => e.CreditCardPaymentId == p.Id && e.Type == PaymentAppWalletEntryType.Earned).Sum(e => e.Amount)
        };

        var dto = new CardHistoryDto();
        foreach (var b in bills)
        {
            var linked = payments.Where(p => p.CreditCardBillId == b.Id).Select(Map).ToList();
            var paid = linked.Sum(p => p.Amount);
            dto.Statements.Add(new CardStatementHistoryDto
            {
                BillId = b.Id,
                StatementDate = b.StatementDate,
                DueDate = b.DueDate,
                BillAmount = b.BillAmount,
                MinimumDue = b.MinimumDue,
                InterestAndFees = b.InterestAndFees,
                OtherCharges = b.OtherCharges,
                Paid = paid,
                Remaining = Math.Max(0, b.BillAmount - paid),
                CashbackTotal = linked.Sum(p => p.CashbackAmount ?? 0),
                Status = paid >= b.BillAmount ? "Paid" : paid > 0 ? "Partly paid" : "Unpaid",
                Payments = linked
            });
        }
        dto.UnlinkedPayments = payments.Where(p => p.CreditCardBillId == null).Select(Map).ToList();
        return Result.Success(dto);
    }

    public async Task<Result<CreditCardBillResultDto>> RecordCreditCardBillAsync(string userId, RecordCreditCardBillDto dto)
    {
        var account = await _context.Accounts
            .Include(a => a.AccountCategory)
            .Include(a => a.CreditCardDetails)
            .FirstOrDefaultAsync(a => a.Id == dto.AccountId);

        if (account == null || account.UserId != userId)
            return Result.Failure<CreditCardBillResultDto>(new Error("Account.NotFound", "Account not found."));

        var isCreditCard = account.AccountCategory?.AccountType == AccountType.CreditCard ||
                           account.CreditCardDetails != null ||
                           (account.AccountCategory?.Name != null && account.AccountCategory.Name.ToLower().Contains("credit"));

        if (!isCreditCard)
            return Result.Failure<CreditCardBillResultDto>(new Error("CreditCard.NotACard", "This account isn't set up as a credit card."));

        if (account.CreditCardDetails == null)
        {
            account.CreditCardDetails = new CreditCardDetails { AccountId = account.Id };
            _context.CreditCardDetails.Add(account.CreditCardDetails);
        }

        if (dto.BillAmount < 0)
            return Result.Failure<CreditCardBillResultDto>(new Error("CreditCardBill.InvalidAmount", "Bill amount can't be negative."));

        var computedBreakdown = await CreditCardStatementCalculator.CalculateAsync(_context, account);
        var statementDate = dto.StatementDate.HasValue
            ? DateTime.SpecifyKind(dto.StatementDate.Value.Date, DateTimeKind.Utc)
            : computedBreakdown.MostRecentStatementDate ?? DateTime.UtcNow;

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
            existingBill.InterestAndFees = Math.Max(0, dto.InterestAndFees ?? 0);
            existingBill.OtherCharges = Math.Max(0, dto.OtherCharges ?? 0);
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
                InterestAndFees = Math.Max(0, dto.InterestAndFees ?? 0),
                OtherCharges = Math.Max(0, dto.OtherCharges ?? 0),
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
        await _cache.InvalidateDashboardAsync(userId);

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

        // Everything below (validation reads AND writes) runs in ONE database
        // transaction, serialised per user with a Postgres advisory lock.
        // Without this, two parallel requests could both spend the same wallet
        // balance or the same bank balance, and a failure between the two
        // SaveChanges calls would leave balances moved but wallet entries /
        // link ids missing. Early `return`s roll the transaction back.
        var db = (DbContext)_context;
        var isPostgres = db.Database.ProviderName?.Contains("Npgsql") == true;
        await using var dbTx = isPostgres ? await db.Database.BeginTransactionAsync() : null;
        if (isPostgres)
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock(hashtext({"cc-payment:" + userId}))");
        }

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

        // Cumulative cash drawn from each paying account across the batch, so
        // two rows from the same account can't both pass a per-row check.
        var cashUsageThisBatch = new Dictionary<int, decimal>();

        foreach (var payment in dto.Payments)
        {
            if (payment.Amount <= 0)
                return Result.Failure<CreditCardPaymentBatchResultDto>(new Error(
                    "CreditCardPayment.InvalidAmount", "Every payment amount must be greater than zero."));

            if (payment.AppliedWalletAmount < 0)
                return Result.Failure<CreditCardPaymentBatchResultDto>(new Error(
                    "CreditCardPayment.InvalidWalletAmount", "The applied wallet amount can't be negative."));

            if (payment.CashbackAmount.HasValue && payment.CashbackAmount.Value < 0)
                return Result.Failure<CreditCardPaymentBatchResultDto>(new Error(
                    "CreditCardPayment.InvalidCashbackAmount", "Cashback can't be negative."));

            if (payment.CashbackAccountId.HasValue)
            {
                var cashbackOwned = await _context.Accounts
                    .AnyAsync(a => a.Id == payment.CashbackAccountId.Value && a.UserId == userId);
                if (!cashbackOwned)
                    return Result.Failure<CreditCardPaymentBatchResultDto>(new Error(
                        "Account.NotFound", "Cashback account not found."));
            }

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

            // The wallet may cover at most the payment itself; whatever it
            // doesn't cover is drawn from the paying account.
            if (payment.AppliedWalletAmount > payment.Amount)
                return Result.Failure<CreditCardPaymentBatchResultDto>(new Error(
                    "CreditCardPayment.WalletDiscountExceedsPayment",
                    "The applied wallet amount can't be more than the payment itself."));

            var actualCashNeeded = payment.Amount - payment.AppliedWalletAmount;
            cashUsageThisBatch.TryGetValue(payment.PayingAccountId, out var cashAlreadyUsed);
            if (payingAccount.Balance < cashAlreadyUsed + actualCashNeeded)
                return Result.Failure<CreditCardPaymentBatchResultDto>(new Error(
                    "CreditCardPayment.InsufficientFunds",
                    $"{payingAccount.Name} has insufficient balance for ₹{(cashAlreadyUsed + actualCashNeeded):F2} of payments. Available: ₹{payingAccount.Balance:F2}"));
            cashUsageThisBatch[payment.PayingAccountId] = cashAlreadyUsed + actualCashNeeded;

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
        // The gap between a recorded bill and tracked purchases is only "interest/fees" when the
        // user said so; otherwise it is card spend that was never entered.
        var gapIsDeclaredInterest = false;
        if (targetBill != null)
        {
            var interestAlreadyPaid = await _context.CreditCardPayments
                .Where(p => p.CreditCardAccountId == dto.CreditCardAccountId && !p.IsDeleted &&
                            (p.CreditCardBillId == targetBill.Id || (p.CreditCardBillId == null && p.Date >= targetBill.StatementDate)))
                .SumAsync(p => (decimal?)p.InterestPortion) ?? 0;

            var totalBillInterest = Math.Max(0, targetBill.BillAmount - computedBreakdown.StatementOutstanding);
            gapIsDeclaredInterest = (targetBill.InterestAndFees + targetBill.OtherCharges) > 0 &&
                                    (targetBill.InterestAndFees + targetBill.OtherCharges) >= totalBillInterest;
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
                    Description = gapIsDeclaredInterest ? $"Interest/fees — {ccAccount.Name}" : $"Card spend not itemised (from statement) — {ccAccount.Name}",
                    Amount = interestPortion,
                    Date = payment.Date,
                    Type = TransactionType.Expense,
                    AccountId = dto.CreditCardAccountId,
                    UserId = userId,
                    Kind = TransactionKind.Normal,
                    TransactionCategoryId = gapIsDeclaredInterest ? defaultInterestCat?.Id : null
                };
                _context.Transactions.Add(interestTx);
                ccAccount.Balance -= interestPortion;
            }

            // 2. Settlement event: Standard transfer pair (Bank Expense, CC Income)
            if (payment.Amount > 0)
            {
                var transferGroupId = Guid.NewGuid();
                var actualCashFromPayingAccount = payment.Amount - payment.AppliedWalletAmount;

                if (actualCashFromPayingAccount > 0)
                {
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
                _context.Transactions.Add(expenseLeg);
                }

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
        if (dbTx != null) await dbTx.CommitAsync();

        await _cache.InvalidateDashboardAsync(userId);

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

    private async Task PopulateCreditCardBillingDetailsAsync(string userId, List<AccountDto> accountDtos)
    {
        var ccDtos = accountDtos
            .Where(a => a.AccountType == AccountType.CreditCard || a.CreditCardDetails != null || (a.AccountCategoryName != null && a.AccountCategoryName.ToLower().Contains("credit")))
            .ToList();

        if (ccDtos.Count == 0) return;

        var ccAccountIds = ccDtos.Select(a => a.Id).ToList();

        var bills = await _context.CreditCardBills
            .Where(b => ccAccountIds.Contains(b.AccountId) && b.UserId == userId)
            .OrderByDescending(b => b.StatementDate)
            .ToListAsync();

        var latestBillByAccount = bills
            .GroupBy(b => b.AccountId)
            .ToDictionary(g => g.Key, g => g.First());

        var payments = await _context.CreditCardPayments
            .Where(p => ccAccountIds.Contains(p.CreditCardAccountId) && !p.IsDeleted && p.UserId == userId)
            .ToListAsync();

        foreach (var acc in ccDtos)
        {
            acc.CreditCardDetails ??= new CreditCardDetailsDto();
            var details = acc.CreditCardDetails;

            latestBillByAccount.TryGetValue(acc.Id, out var latestBill);

            decimal totalPaidForCycle = 0;
            if (latestBill != null)
            {
                totalPaidForCycle = payments
                    .Where(p => p.CreditCardAccountId == acc.Id &&
                                (p.CreditCardBillId == latestBill.Id || (p.CreditCardBillId == null && p.Date >= latestBill.StatementDate)))
                    .Sum(p => p.Amount);
            }

            var billAmount = latestBill?.BillAmount;
            var remainingBillAmount = billAmount.HasValue
                ? Math.Max(0, billAmount.Value - totalPaidForCycle)
                : Math.Max(0, Math.Abs(acc.Balance) - totalPaidForCycle);

            var isBillFullyPaid = latestBill != null
                ? ((billAmount.HasValue && billAmount.Value > 0) ? (totalPaidForCycle >= billAmount.Value) : (latestBill.IsPaid && remainingBillAmount <= 0))
                : (acc.Balance >= 0);

            var isPaid = isBillFullyPaid || (acc.Balance >= 0 && (!billAmount.HasValue || remainingBillAmount <= 0));

            details.LatestBillAmount = billAmount;
            details.LatestBillIsPaid = isBillFullyPaid;
            details.RemainingBillAmount = remainingBillAmount;
            details.TotalPaidForCycle = totalPaidForCycle;
            details.IsPaid = isPaid;
            details.LastStatementDate = latestBill?.StatementDate;

            if (latestBill?.MinimumDue.HasValue == true)
            {
                details.MinimumDueAmount = latestBill.MinimumDue;
            }
            if (latestBill?.DueDate.HasValue == true)
            {
                details.DueDate = latestBill.DueDate;
            }
        }
    }
}
