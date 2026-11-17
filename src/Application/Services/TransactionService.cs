namespace Application.Services;


using Application.Common.Helpers;
using Application.Common.Models;
using Application.Contracts;
using Application.DTOs.Auth;
using Application.DTOs.Budgets;
using Application.DTOs.Transactions;
using Domain.Entities;
using Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

public class TransactionService : ITransactionService
{
    private readonly IApplicationDbContext _context;
    private readonly ICacheService _cache;
    private readonly ILogger<TransactionService> _logger;
    private readonly IEmailService _emailService;
    private readonly IUserSettingsService _userSettings;

    public TransactionService(
        IApplicationDbContext context,
        ICacheService cache,
        ILogger<TransactionService> logger,
        IEmailService emailService,
        IUserSettingsService userSettings)
    {
        _context = context;
        _cache = cache;
        _logger = logger;
        _emailService = emailService;
        _userSettings = userSettings;
    }

    public async Task<Result<PaginatedResult<TransactionDto>>> GetTransactionsAsync(string userId, int accountId, QueryParameters queryParams)
    {
        var account = await _context.Accounts.FindAsync(accountId);
        if (account == null || account.UserId != userId)
            return Result.Failure<PaginatedResult<TransactionDto>>(new Error("Account.NotFound", "Account not found."));

        var queryable = _context.Transactions
            .Where(t => t.AccountId == accountId)
            .AsQueryable();

        // ── Month/year filtering — consolidated into one timezone-correct path ──
        // Both the Filters-dict style and the typed Month/Year style ended up
        // doing the same job with two different (both buggy) implementations.
        // One local helper, used by whichever the caller actually sent.
        int? filterMonth = null;
        int? filterYear = null;

        if (queryParams.Filters.TryGetValue("month", out var monthStr) && int.TryParse(monthStr, out var m))
            filterMonth = m;
        if (queryParams.Filters.TryGetValue("year", out var yearStr) && int.TryParse(yearStr, out var y))
            filterYear = y;

        // Typed params take precedence if both happen to be sent — arbitrary
        // but consistent choice, and in practice only one path will be used.
        if (queryParams.Month.HasValue) filterMonth = queryParams.Month.Value;
        if (queryParams.Year.HasValue) filterYear = queryParams.Year.Value;

        if (filterMonth.HasValue && filterYear.HasValue)
        {
            var localMonthStart = new DateTime(filterYear.Value, filterMonth.Value, 1, 0, 0, 0, DateTimeKind.Unspecified);
            var localMonthEnd = localMonthStart.AddMonths(1).AddTicks(-1);
            var utcMonthStart = localMonthStart.ToUtc();
            var utcMonthEnd = localMonthEnd.ToUtc();

            queryable = queryable.Where(t => t.Date >= utcMonthStart && t.Date <= utcMonthEnd);
        }

        // Explicit date range (e.g. a credit-card statement cycle). Local calendar dates, inclusive;
        // overrides month/year. Either end may be omitted for an open range.
        if (queryParams.Filters.TryGetValue("from", out var fromStr) || queryParams.Filters.TryGetValue("to", out var toStr0))
        {
            DateTime fromLocal = default, toLocal = default;
            var hasFrom = DateTime.TryParseExact(fromStr, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out fromLocal);
            var hasTo = queryParams.Filters.TryGetValue("to", out var toStr) &&
                DateTime.TryParseExact(toStr, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out toLocal);
            if (hasFrom || hasTo)
            {
                // Replace the calendar-month window applied above.
                queryable = _context.Transactions.Where(t => t.AccountId == accountId).AsQueryable();
                if (hasFrom)
                {
                    var utcFrom = DateTime.SpecifyKind(fromLocal.Date, DateTimeKind.Unspecified).ToUtc();
                    queryable = queryable.Where(t => t.Date >= utcFrom);
                }
                if (hasTo)
                {
                    var utcTo = DateTime.SpecifyKind(toLocal.Date.AddDays(1).AddTicks(-1), DateTimeKind.Unspecified).ToUtc();
                    queryable = queryable.Where(t => t.Date <= utcTo);
                }
            }
        }

        // Category Filter
        if (queryParams.Filters.TryGetValue("categoryName", out var categoryName) && !string.IsNullOrWhiteSpace(categoryName))
        {
            queryable = queryable.Where(t => t.TransactionCategory != null && t.TransactionCategory.Name == categoryName);
        }

        if (queryParams.Filters.TryGetValue("tag", out var tagFilter) && !string.IsNullOrWhiteSpace(tagFilter))
        {
            var like = TransactionTags.Pattern(tagFilter);
            // Explicit escape character: the 2-argument overload becomes "ESCAPE ''" on Npgsql, which switches escaping off
            // and made tags containing _ or % unfindable.
            queryable = queryable.Where(t => t.Tags != null && EF.Functions.Like(t.Tags, like, "\\"));
        }

        if (!string.IsNullOrWhiteSpace(queryParams.GlobalSearch))
        {
            var search = queryParams.GlobalSearch.Trim().ToLower();
            queryable = queryable.Where(t => t.Description.ToLower().Contains(search) ||
                (t.TransactionCategory != null && t.TransactionCategory.Name.ToLower().Contains(search)) ||
                (t.Notes != null && t.Notes.ToLower().Contains(search)) ||
                (t.Tags != null && t.Tags.Contains(search)));
        }

        if (queryParams.TransactionCategoryId.HasValue)
        {
            var categoryIds = await CategoryRollup.ExpandCategoryIdsAsync(_context, queryParams.TransactionCategoryId.Value);
            queryable = queryable.Where(t => t.TransactionCategoryId.HasValue && categoryIds.Contains(t.TransactionCategoryId.Value));
        }

        // FR-03: amount range filtering — confirmed missing during the synopsis
        // audit, this closes that gap. Inclusive on both bounds, either end
        // can be omitted independently (e.g. "over ₹5000" with no upper bound).
        if (queryParams.MinAmount.HasValue)
        {
            queryable = queryable.Where(t => t.Amount >= queryParams.MinAmount.Value);
        }
        if (queryParams.MaxAmount.HasValue)
        {
            queryable = queryable.Where(t => t.Amount <= queryParams.MaxAmount.Value);
        }

        // Newest first is the default and the order the (AccountId, Date DESC, Id DESC) index serves directly.
        var isDefaultOrder = queryParams.SortBy == null || (queryParams.SortBy.Equals("date", StringComparison.OrdinalIgnoreCase) && queryParams.SortOrder != "asc");
        var useCursor = isDefaultOrder && Keyset.TryDecode(queryParams.Cursor, out _, out _);

        // The total is only worked out for the first page / a page-number jump. A cursor request (the next page of an
        // infinite list) skips the COUNT and the OFFSET completely: that is what keeps page 5000 as fast as page 2.
        var totalRecords = useCursor ? -1 : await queryable.CountAsync();

        if (useCursor)
        {
            Keyset.TryDecode(queryParams.Cursor, out var afterDate, out var afterId);
            queryable = queryable.After(t => t.Date, t => t.Id, afterDate, afterId);
        }

        // Apply dynamic sorting
        queryable = queryParams.SortBy?.ToLower() switch
        {
            "date" => queryParams.SortOrder == "asc" ? queryable.OrderBy(t => t.Date).ThenByDescending(t => t.Id) : queryable.OrderByDescending(t => t.Date).ThenByDescending(t => t.Id),
            "amount" => queryParams.SortOrder == "desc" ? queryable.OrderByDescending(t => t.Amount).ThenByDescending(t => t.Id) : queryable.OrderBy(t => t.Amount).ThenByDescending(t => t.Id),
            "category" or "categoryname" => queryParams.SortOrder == "desc" ? queryable.OrderByDescending(t => t.TransactionCategory != null ? t.TransactionCategory.Name.ToLower() : "").ThenByDescending(t => t.Id) : queryable.OrderBy(t => t.TransactionCategory != null ? t.TransactionCategory.Name.ToLower() : "").ThenByDescending(t => t.Id),
            "description" => queryParams.SortOrder == "desc" ? queryable.OrderByDescending(t => t.Description.ToLower()).ThenByDescending(t => t.Id) : queryable.OrderBy(t => t.Description.ToLower()).ThenByDescending(t => t.Id),
            _ => queryable.OrderByDescending(t => t.Date).ThenByDescending(t => t.Id) // Default
        };

        // One extra row tells us whether another page exists, without counting.
        var skip = useCursor ? 0 : (queryParams.PageNumber - 1) * queryParams.PageSize;
        var rows = await queryable
            .Include(t => t.TransactionCategory)
            .Include(t => t.Account)
            .Include(t => t.Merchant)
            .Skip(skip)
            .Take(queryParams.PageSize + 1)
            .ToListAsync();

        var hasMore = rows.Count > queryParams.PageSize;
        var items = hasMore ? rows.Take(queryParams.PageSize).ToList() : rows;

        var pagedDto = new PaginatedResult<TransactionDto>(
            items.Select(MapToDto).ToList(),
            totalRecords,
            queryParams.PageNumber,
            queryParams.PageSize
        )
        {
            HasMore = hasMore,
            // Only the default order can be continued by cursor; other sorts keep using page numbers.
            NextCursor = isDefaultOrder && hasMore && items.Count > 0 ? Keyset.Encode(items[^1].Date, items[^1].Id) : null
        };
        return Result.Success(pagedDto);
    }

    public async Task<Result<BulkInsertResponseDto>> BulkUpsertTransactionsAsync(string userId, BulkTransactionPayloadDto payload)
    {
        var response = new BulkInsertResponseDto();

        if (payload.Transactions.Count == 0)
            return Result.Failure<BulkInsertResponseDto>(new Error("Bulk.Empty", "Nothing to save."));
        if (payload.Transactions.Count > 500)
            return Result.Failure<BulkInsertResponseDto>(new Error("Bulk.TooMany", "At most 500 rows can be saved at once."));

        // Double-click / retry guard: the same client batch id is only ever applied once.
        var batchKey = string.IsNullOrWhiteSpace(payload.BatchId) ? null : $"bulk::{userId}::{payload.BatchId}";
        if (batchKey != null && await _cache.GetAsync<string>(batchKey) != null)
            return Result.Failure<BulkInsertResponseDto>(new Error("Bulk.AlreadyProcessed",
                "This batch was already saved. Refresh the transaction list to check before adding it again."));

        var bulkDb = (DbContext)_context;
        using var dbTransaction = await bulkDb.Database.BeginTransactionAsync();

        try
        {
            // Serialise identical submits (double click / retry / two tabs): the second one waits here, then
            // sees the key written by the first and stops. The lock is released by commit/rollback.
            if (bulkDb.Database.ProviderName?.Contains("Npgsql") == true && batchKey != null)
            {
                await bulkDb.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtext({batchKey}))");
                if (await _cache.GetAsync<string>(batchKey) != null)
                {
                    await dbTransaction.RollbackAsync();
                    return Result.Failure<BulkInsertResponseDto>(new Error("Bulk.AlreadyProcessed",
                        "This batch was already saved. Refresh the transaction list to check before adding it again."));
                }
            }

            // Apply in date order (credits before debits on the same day) so the running-balance
            // check follows the timeline instead of the order the rows happen to be typed in.
            // Failure indexes still refer to the original row positions.
            var order = Enumerable.Range(0, payload.Transactions.Count)
                .OrderBy(k => payload.Transactions[k].Transaction.Date.Date)
                .ThenBy(k => (payload.Transactions[k].DestinationAccountId.HasValue ||
                              payload.Transactions[k].Transaction.Type == TransactionType.Expense) ? 1 : 0)
                .ThenBy(k => k)
                .ToList();

            foreach (var i in order)
            {
                var entry      = payload.Transactions[i];
                bool rowOk     = false;
                string rowErr  = string.Empty;

                if (entry.DestinationAccountId.HasValue)
                {
                    var transferDto = new CreateTransferDto
                    {
                        Amount                = entry.Transaction.Amount,
                        Date                  = entry.Transaction.Date,
                        DestinationAccountId  = entry.DestinationAccountId.Value,
                        Description           = entry.Transaction.Description ?? "Transfer",
                        TransactionCategoryId = entry.Transaction.TransactionCategoryId
                    };
                    var result = await CreateTransferAsync(userId, entry.AccountId, transferDto);
                    rowOk  = result.IsSuccess;
                    if (!rowOk) rowErr = result.Error.Description;
                }
                else
                {
                    var result = await UpsertTransactionAsync(userId, entry.AccountId, entry.Transaction);
                    rowOk  = result.IsSuccess;
                    if (!rowOk) rowErr = result.Error.Description;
                }

                if (rowOk)
                {
                    response.SuccessfulCount++;
                }
                else
                {
                    response.FailedCount++;
                    response.FailedTransactions.Add(new BulkInsertFailureDto
                    {
                        Index  = i,
                        Errors = new List<string> { rowErr }
                    });
                }
            }

            // ── FIX: only commit when EVERY row succeeded ─────────────────────
            if (response.FailedCount == 0)
            {
                // Key is written BEFORE the commit while the advisory lock is still held, so a waiting duplicate
                // can never slip between the commit and the key. Removed again if the commit itself fails.
                if (batchKey != null) await _cache.SetAsync(batchKey, "done", TimeSpan.FromMinutes(30));
                try { await dbTransaction.CommitAsync(); }
                catch { if (batchKey != null) await _cache.RemoveAsync(batchKey); throw; }
                await _cache.InvalidateDashboardAsync(userId);
            }
            else
            {
                // Roll back everything — no partial financial state
                await dbTransaction.RollbackAsync();
                response.SuccessfulCount = 0; // none were actually committed
                response.RolledBack = true;
                _logger.LogWarning(
                    "BulkUpsert rolled back for user {UserId}. {Failed}/{Total} rows failed.",
                    userId, response.FailedCount, payload.Transactions.Count);
            }

            return Result.Success(response);
        }
        catch (Exception ex)
        {
            await dbTransaction.RollbackAsync();
            _logger.LogError(ex, "BulkUpsert threw unexpectedly for user {UserId}", userId);
            return Result.Failure<BulkInsertResponseDto>(
                new Error("BulkInsert.Error", "An unexpected error occurred during bulk insert."));
        }
    }


    /// <summary>True when an identical row was written within the user's duplicate-guard window (default 3 s) — a double click / retry.
    /// Skipped inside explicit transactions (bulk), where identical rows are legitimate.</summary>
    private async Task<bool> IsRapidDuplicateAsync(string userId, int accountId, decimal amount,
        TransactionType type, DateTime date, string? description, bool transfer)
    {
        if (((DbContext)_context).Database.CurrentTransaction != null) return false;
        var guardSeconds = (await _userSettings.GetTransactionSettingsAsync(userId)).DuplicateGuardSeconds; // user-tunable, default 3
        if (guardSeconds <= 0) return false;
        var since = DateTime.UtcNow.AddSeconds(-guardSeconds);
        var d = date.EnsureUtc();
        return await _context.Transactions.AnyAsync(t =>
            t.UserId == userId && !t.IsDeleted && t.AccountId == accountId &&
            t.Amount == amount && t.Type == type && t.Date == d &&
            t.Description == description && t.CreatedAt >= since &&
            (transfer ? t.TransferGroupId != null : t.TransferGroupId == null));
    }

    public async Task<Result<TransactionDto>> UpsertTransactionAsync(string userId, int accountId, UpsertTransactionDto dto)
    {
        // A negative amount here would silently invert the transaction's
        // real effect — an "Expense" with a negative amount increases
        // the balance instead of decreasing it, effectively becoming
        // income while still labeled and categorized as an expense.
        // Rejected here, once, protects every path that creates or
        // edits a transaction through this shared method.
        if (dto.Amount <= 0)
            return Result.Failure<TransactionDto>(new Error(
                "Transaction.InvalidAmount", "Amount must be greater than zero."));

        if (!string.IsNullOrWhiteSpace(dto.Description))
            dto.Description = ToTitleCase(dto.Description);

        var account = await _context.Accounts
            .Include(a => a.AccountCategory)
            .FirstOrDefaultAsync(a => a.Id == accountId);
        if (account == null || account.UserId != userId)
            return Result.Failure<TransactionDto>(new Error("Account.NotFound", "Account not found."));

        if (dto.TransactionCategoryId.HasValue)
        {
            var categoryBelongsToUser = await _context.TransactionCategories
                .AnyAsync(c => c.Id == dto.TransactionCategoryId.Value && c.UserId == userId);
            if (!categoryBelongsToUser)
                return Result.Failure<TransactionDto>(new Error(
                    "Transaction.InvalidCategory", "That category doesn't belong to you."));
        }

        if (dto.MerchantId.HasValue)
        {
            var merchantBelongsToUser = await _context.Merchants
                .AnyAsync(m => m.Id == dto.MerchantId.Value && m.UserId == userId);
            if (!merchantBelongsToUser)
                return Result.Failure<TransactionDto>(new Error(
                    "Transaction.InvalidMerchant", "That merchant doesn't belong to you."));
        }

        if (!(dto.Id.HasValue && dto.Id.Value > 0) &&
            await IsRapidDuplicateAsync(userId, accountId, dto.Amount, dto.Type, dto.Date, dto.Description, false))
            return Result.Failure<TransactionDto>(new Error("Transaction.DuplicateSubmit",
                "That exact transaction was just saved. If you meant to add it twice, wait a few seconds and try again."));

        Transaction? transaction = null;
        int? syncCardPaymentAccountId = null;
        decimal oldAmount = 0;
        TransactionType oldType = TransactionType.Expense;

        if (dto.Id.HasValue && dto.Id.Value > 0)
        {
            transaction = await _context.Transactions.FindAsync(dto.Id.Value);
            if (transaction == null || transaction.AccountId != accountId)
                return Result.Failure<TransactionDto>(new Error("Transaction.NotFound", "Transaction not found."));

            if (transaction.Kind == TransactionKind.Transfer || transaction.Kind == TransactionKind.LoanPrincipal)
            {
                return Result.Failure<TransactionDto>(new Error(
                    "Transaction.PartOfPair",
                    "This transaction is one side of a transfer or loan payment and can't be deleted on its own — deleting only one leg would leave the accounting inconsistent. Contact support or use the account merge/correction tools if this needs reversing."));
            }

            // Rows created by a credit-card payment are mirrored in the payment record (PH1-08).
            //  • Interest/fee row: its amount CAN be corrected (the bank charged ₹250, not ₹300) — the payment's
            //    InterestPortion and the bill's stored paid/remaining follow it. Its type can't change.
            //  • Cashback row: amount/type/date are locked, because wallet and cashback ledgers depend on them.
            var linkedPayment = await _context.CreditCardPayments.FirstOrDefaultAsync(ccp =>
                ccp.InterestTransactionId == transaction.Id || ccp.CashbackTransactionId == transaction.Id);
            if (linkedPayment != null)
            {
                var isInterestRow = linkedPayment.InterestTransactionId == transaction.Id;
                var amountChanged = transaction.Amount != dto.Amount;
                var typeChanged = transaction.Type != dto.Type;
                var dateChanged = transaction.Date.Date != dto.Date.EnsureUtc().Date;
                if (typeChanged || (!isInterestRow && (amountChanged || dateChanged)))
                {
                    return Result.Failure<TransactionDto>(new Error(
                        "Transaction.CardPaymentRow",
                        isInterestRow
                            ? "This row is the interest/fee charge of a credit card payment, so it must stay an expense. You can change its amount, date, description, category and notes."
                            : "This row is the cashback of a credit card payment, so its amount, type and date can't be edited on their own. You can still change its description, category, notes and tags. To change the amounts, delete the card payment (you can undo it for 10 minutes) and record it again."));
                }
                if (isInterestRow && amountChanged)
                {
                    if (dto.Amount > linkedPayment.Amount)
                        return Result.Failure<TransactionDto>(new Error(
                            "Transaction.CardPaymentRow",
                            "The interest/fee can't be more than the payment it came from."));
                    linkedPayment.InterestPortion = dto.Amount;
                    linkedPayment.PrincipalPortion = linkedPayment.Amount - dto.Amount;
                    syncCardPaymentAccountId = linkedPayment.CreditCardAccountId;
                }
            }

            oldAmount = transaction.Amount;
            oldType = transaction.Type;
            transaction.Description = dto.Description;
            transaction.Amount = dto.Amount;
            transaction.Date = dto.Date.EnsureUtc();
            transaction.Type = dto.Type;
            transaction.TransactionCategoryId = dto.TransactionCategoryId;
            transaction.MerchantId = dto.MerchantId;
            transaction.Notes = TransactionTags.CleanNotes(dto.Notes);
            transaction.Tags = TransactionTags.Pack(dto.Tags);
            _context.Transactions.Update(transaction);
        }
        else
        {
            // Rules fill ONLY the fields left empty (category, merchant, tags) on a brand-new transaction.
            if (!dto.TransactionCategoryId.HasValue || !dto.MerchantId.HasValue || (dto.Tags == null || dto.Tags.Count == 0))
            {
                var ruleHit = await TransactionRuleService.FindMatchAsync(_context, userId, dto.Description, dto.Amount, dto.Type);
                if (ruleHit != null)
                {
                    if (!dto.TransactionCategoryId.HasValue) dto.TransactionCategoryId = ruleHit.TransactionCategoryId;
                    if (!dto.MerchantId.HasValue) dto.MerchantId = ruleHit.MerchantId;
                    if (dto.Tags == null || dto.Tags.Count == 0) dto.Tags = TransactionTags.Unpack(ruleHit.Tags);
                }
            }
            transaction = new Transaction
            {
                Description = dto.Description,
                Amount = dto.Amount,
                Date = dto.Date.EnsureUtc(),
                Type = dto.Type,
                TransactionCategoryId = dto.TransactionCategoryId,
                MerchantId = dto.MerchantId,
                Notes = TransactionTags.CleanNotes(dto.Notes),
                Tags = TransactionTags.Pack(dto.Tags),
                AccountId = accountId,
                UserId = userId,
                RecurringTransactionId = dto.RecurringTransactionId,
                RecurringOccurrenceDate = dto.RecurringOccurrenceDate?.EnsureUtc(),
                ImportRef = dto.ImportRef,
                ReconciledAt = dto.ReconciledAt?.EnsureUtc()
            };
            _context.Transactions.Add(transaction);
        }

        // Adjust Account Balance — compute first, check, THEN apply.
        var revertedBalance = account.Balance + (oldType == TransactionType.Income ? -oldAmount : oldAmount);
        var prospectiveBalance = revertedBalance + (transaction.Type == TransactionType.Income ? transaction.Amount : -transaction.Amount);

        bool isLiability = account.AccountCategory?.IsLiability ?? false;
        if (!isLiability && prospectiveBalance < 0)
        {
            return Result.Failure<TransactionDto>(new Error(
                "Transaction.InsufficientFunds",
                $"This would take {account.Name} to a negative balance (₹{prospectiveBalance:F2}). Insufficient funds for this expense."));
        }

        account.Balance = prospectiveBalance;
        _context.Accounts.Update(account);
        
        var saveResult = await ConcurrencySafeSave.TrySaveChangesAsync(_context);
        if (saveResult.IsFailure)
            return Result.Failure<TransactionDto>(saveResult.Error);

        if (syncCardPaymentAccountId.HasValue)
        {
            await CreditCardBillMath.RecomputeStoredAsync(_context, syncCardPaymentAccountId.Value);
            await _context.SaveChangesAsync();
        }

        // Only check for NEW expense transactions — see design notes above
        // for why edits are deliberately out of scope for this check.
        List<BudgetAlertDto>? budgetAlerts = null;
        if (!dto.Id.HasValue && transaction.Type == TransactionType.Expense)
        {
            try
            {
                budgetAlerts = await CheckBudgetAlertsAsync(userId, transaction);
            }
            catch (Exception ex)
            {
                // Never let an alert failure fail the transaction save itself.
                _logger.LogWarning(ex, "Overspend alert check failed for transaction {TransactionId} — transaction itself saved successfully.", transaction.Id);
            }
        }

        // Reload to include relations for return DTO
        var resultTransactionWithCategory = await _context.Transactions
            .Include(t => t.TransactionCategory)
            .Include(t => t.Merchant)
            .FirstAsync(t => t.Id == transaction.Id);

        var resultDto = MapToDto(resultTransactionWithCategory);
        if (budgetAlerts is { Count: > 0 }) resultDto.BudgetAlerts = budgetAlerts;
        await _cache.InvalidateDashboardAsync(userId); // invalidates summary, category, insights

        return Result.Success(resultDto);
    }

    public async Task<Result<bool>> DeleteTransactionAsync(string userId, int accountId, int transactionId)
    {
        var account = await _context.Accounts.FindAsync(accountId);
        if (account == null || account.UserId != userId)
            return Result.Failure<bool>(new Error("Account.NotFound", "Account not found."));

        var transaction = await _context.Transactions.FindAsync(transactionId);
        if (transaction == null || transaction.AccountId != accountId)
            return Result.Failure<bool>(new Error("Transaction.NotFound", "Transaction not found."));

        var relatedTransactionIds = new HashSet<int> { transaction.Id };

        // 1. Check TransferGroupId
        if (transaction.TransferGroupId.HasValue)
        {
            var paired = await _context.Transactions
                .Where(t => t.TransferGroupId == transaction.TransferGroupId.Value && !t.IsDeleted)
                .Select(t => t.Id)
                .ToListAsync();
            foreach (var id in paired) relatedTransactionIds.Add(id);
        }

        // 2. Check CreditCardPayment records linked to this transaction or any paired transaction
        var ccPayments = await _context.CreditCardPayments
            .Where(p => (p.PrincipalIncomeTransactionId.HasValue && relatedTransactionIds.Contains(p.PrincipalIncomeTransactionId.Value)) ||
                        (p.PrincipalExpenseTransactionId.HasValue && relatedTransactionIds.Contains(p.PrincipalExpenseTransactionId.Value)) ||
                        (p.InterestTransactionId.HasValue && relatedTransactionIds.Contains(p.InterestTransactionId.Value)) ||
                        (p.CashbackTransactionId.HasValue && relatedTransactionIds.Contains(p.CashbackTransactionId.Value)))
            .ToListAsync();

        var affectedBillIds = new HashSet<int>();

        foreach (var p in ccPayments)
        {
            if (p.PrincipalIncomeTransactionId.HasValue) relatedTransactionIds.Add(p.PrincipalIncomeTransactionId.Value);
            if (p.PrincipalExpenseTransactionId.HasValue) relatedTransactionIds.Add(p.PrincipalExpenseTransactionId.Value);
            if (p.InterestTransactionId.HasValue) relatedTransactionIds.Add(p.InterestTransactionId.Value);
            if (p.CashbackTransactionId.HasValue) relatedTransactionIds.Add(p.CashbackTransactionId.Value);

            if (p.CreditCardBillId.HasValue)
            {
                affectedBillIds.Add(p.CreditCardBillId.Value);
            }

            _context.CreditCardPayments.Remove(p);
        }

        // 3. Load all transactions to delete and revert balances
        var allTxsToDelete = await _context.Transactions
            .Where(t => relatedTransactionIds.Contains(t.Id) && !t.IsDeleted)
            .ToListAsync();

        // 3a. Loan payment rollback: a payment is stored as a principal pair
        // (linked by TransferGroupId) PLUS a separate LoanInterest expense on
        // the paying account with no link. Deleting the principal pair alone
        // used to leave the interest expense behind and the loan's next EMI
        // date advanced. Find the matching interest row (same paying
        // account, same instant, "Interest — <loan>") and remove it too, and
        // step NextEmiDueDate back one month per payment removed.
        var loanLegs = allTxsToDelete
            .Where(t => t.Kind == TransactionKind.LoanPrincipal && t.Type == TransactionType.Income)
            .ToList();
        foreach (var leg in loanLegs)
        {
            var loanAcc = await _context.Accounts
                .Include(a => a.LoanDetails)
                .FirstOrDefaultAsync(a => a.Id == leg.AccountId);
            var payerLeg = allTxsToDelete.FirstOrDefault(t =>
                t.TransferGroupId == leg.TransferGroupId && t.Id != leg.Id);
            if (loanAcc == null || payerLeg == null) continue;

            var interestDesc = $"Interest — {loanAcc.Name}";
            // Payments made since V60 carry a PaymentGroupId, so the interest row is found exactly.
            // Older payments have none: fall back to matching on account + instant + description.
            var interestTx = leg.PaymentGroupId.HasValue
                ? await _context.Transactions.FirstOrDefaultAsync(t =>
                    t.UserId == userId && !t.IsDeleted &&
                    t.Kind == TransactionKind.LoanInterest &&
                    t.PaymentGroupId == leg.PaymentGroupId &&
                    !relatedTransactionIds.Contains(t.Id))
                : await _context.Transactions.FirstOrDefaultAsync(t =>
                    t.UserId == userId && !t.IsDeleted &&
                    t.Kind == TransactionKind.LoanInterest &&
                    t.AccountId == payerLeg.AccountId &&
                    t.Date == leg.Date &&
                    t.Description == interestDesc &&
                    !relatedTransactionIds.Contains(t.Id));
            if (interestTx != null)
            {
                relatedTransactionIds.Add(interestTx.Id);
                allTxsToDelete.Add(interestTx);
            }

            if (loanAcc.LoanDetails?.NextEmiDueDate != null)
                loanAcc.LoanDetails.NextEmiDueDate = loanAcc.LoanDetails.NextEmiDueDate.Value.AddMonths(-1);
        }

        // 3b. Work out the balance effect of every deletion per account FIRST
        // and refuse if it would push a normal (non-liability) account below
        // zero — deleting an income used to be able to do exactly that.
        var deltas = new Dictionary<int, decimal>();
        foreach (var tx in allTxsToDelete)
        {
            var d = tx.Type == TransactionType.Income ? -tx.Amount : tx.Amount;
            deltas[tx.AccountId] = deltas.GetValueOrDefault(tx.AccountId) + d;
        }
        var touchedIds = deltas.Keys.ToList();
        var touched = await _context.Accounts
            .Include(a => a.AccountCategory)
            .Where(a => touchedIds.Contains(a.Id) && a.UserId == userId)
            .ToListAsync();
        foreach (var acc in touched)
        {
            var after = acc.Balance + deltas[acc.Id];
            bool liability = acc.AccountCategory?.IsLiability ?? false;
            if (!liability && after < 0)
                return Result.Failure<bool>(new Error(
                    "Transaction.DeleteWouldOverdraw",
                    $"Deleting this would take {acc.Name} to a negative balance (₹{after:F2}). Delete or adjust the later expenses first."));
        }
        foreach (var acc in touched)
        {
            acc.Balance += deltas[acc.Id];
            _context.Accounts.Update(acc);
        }
        foreach (var tx in allTxsToDelete)
        {
            tx.IsDeleted = true;
            tx.DeletedAt = DateTime.UtcNow;
            _context.Transactions.Update(tx);
        }

        // 4. Re-evaluate affected CreditCardBill.IsPaid states
        var deletingPaymentIds = ccPayments.Select(cp => cp.Id).ToHashSet();
        foreach (var billId in affectedBillIds)
        {
            var bill = await _context.CreditCardBills.FindAsync(billId);
            if (bill != null)
            {
                var remainingTotalPaid = await _context.CreditCardPayments
                    .Where(p => p.CreditCardBillId == billId && !deletingPaymentIds.Contains(p.Id))
                    .SumAsync(p => (decimal?)p.Amount) ?? 0;

                bill.IsPaid = remainingTotalPaid >= bill.BillAmount;
                _context.CreditCardBills.Update(bill);
            }
        }

        var saveResult = await ConcurrencySafeSave.TrySaveChangesAsync(_context);
        if (saveResult.IsFailure)
            return Result.Failure<bool>(saveResult.Error);

        // Stored paid/remaining on the bills of any card whose payment was just removed (PH1-03).
        if (ccPayments.Count > 0)
        {
            foreach (var cardId in ccPayments.Select(cp => cp.CreditCardAccountId).Distinct())
                await CreditCardBillMath.RecomputeStoredAsync(_context, cardId);
            await _context.SaveChangesAsync();
        }

        await _cache.InvalidateDashboardAsync(userId);

        return Result.Success(true);
    }

    public async Task<Result<List<TagCountDto>>> GetTagsAsync(string userId)
    {
        var packed = await _context.Transactions.AsNoTracking()
            .Where(t => t.UserId == userId && !t.IsDeleted && t.Tags != null)
            .Select(t => t.Tags!)
            .ToListAsync();
        var counts = new Dictionary<string, int>();
        foreach (var p in packed)
            foreach (var tag in TransactionTags.Unpack(p))
                counts[tag] = counts.GetValueOrDefault(tag) + 1;
        return Result.Success(counts
            .OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key)
            .Select(kv => new TagCountDto { Tag = kv.Key, Count = kv.Value }).ToList());
    }

    public async Task<Result<bool>> RestoreTransactionAsync(string userId, int accountId, int transactionId)
    {
        var tx = await _context.Transactions.IgnoreQueryFilters()
            .Include(t => t.Account).ThenInclude(a => a.AccountCategory)
            .FirstOrDefaultAsync(t => t.Id == transactionId && t.AccountId == accountId && t.UserId == userId);
        if (tx == null || !tx.IsDeleted)
            return Result.Failure<bool>(new Error("Transaction.NotFound", "Nothing to restore."));

        if (tx.DeletedAt is null || tx.DeletedAt < DateTime.UtcNow - UndoRules.Window)
            return Result.Failure<bool>(new Error("Transaction.UndoExpired", "Undo is only available for a few minutes after deleting."));

        if (tx.TransferGroupId != null || tx.Kind != TransactionKind.Normal)
            return Result.Failure<bool>(new Error("Transaction.UndoNotSupported", "Transfers, payments and system entries can't be undone."));

        var linkedToPayment = await _context.CreditCardPayments.IgnoreQueryFilters().AnyAsync(p =>
            p.CashbackTransactionId == tx.Id || p.InterestTransactionId == tx.Id ||
            p.PrincipalIncomeTransactionId == tx.Id || p.PrincipalExpenseTransactionId == tx.Id);
        if (linkedToPayment)
            return Result.Failure<bool>(new Error("Transaction.UndoNotSupported", "This entry belongs to a card payment and can't be undone on its own."));

        var acc = tx.Account;
        var delta = tx.Type == TransactionType.Income ? tx.Amount : -tx.Amount;
        var liability = acc.AccountCategory?.IsLiability ?? false;
        if (!liability && acc.Balance + delta < 0)
            return Result.Failure<bool>(new Error("Transaction.RestoreWouldOverdraw",
                $"Restoring this would take {acc.Name} to a negative balance."));

        acc.Balance += delta;
        tx.IsDeleted = false;
        tx.DeletedAt = null;
        _context.Accounts.Update(acc);
        _context.Transactions.Update(tx);

        var save = await ConcurrencySafeSave.TrySaveChangesAsync(_context);
        if (save.IsFailure) return Result.Failure<bool>(save.Error);

        await _cache.InvalidateDashboardAsync(userId);
        return Result.Success(true);
    }

    public async Task<Result<bool>> CreateTransferAsync(string userId, int sourceAccountId, CreateTransferDto dto)
    {
        // ── Load source account ────────────────────────────────────────────────
        var sourceAccount = await _context.Accounts
            .Include(a => a.AccountCategory)
            .FirstOrDefaultAsync(a => a.Id == sourceAccountId);

        if (sourceAccount == null || sourceAccount.UserId != userId)
            return Result.Failure<bool>(new Error("Account.NotFound", "Source account not found."));

        var destinationAccount = await _context.Accounts.FindAsync(dto.DestinationAccountId);
        if (destinationAccount == null || destinationAccount.UserId != userId)
            return Result.Failure<bool>(new Error("Account.NotFound", "Destination account not found."));

        if (sourceAccountId == dto.DestinationAccountId)
            return Result.Failure<bool>(new Error("Transfer.SameAccount", "Cannot transfer to the same account."));

        // The controller's [Range] attribute does not run for the bulk-import path, so enforce
        // it here too: a zero/negative transfer would otherwise move money the wrong way.
        if (dto.Amount <= 0)
            return Result.Failure<bool>(new Error("Transfer.InvalidAmount", "Transfer amount must be greater than zero."));

        if (dto.TransactionCategoryId.HasValue)
        {
            var categoryOwned = await _context.TransactionCategories
                .AnyAsync(c => c.Id == dto.TransactionCategoryId.Value && c.UserId == userId);
            if (!categoryOwned)
                return Result.Failure<bool>(new Error("Category.NotFound", "Category not found."));
        }

        // ── Balance check (skip for liability accounts — Credit Card, Loan, etc.) ──
        // Was string-matching on category.Name before; now uses the same
        // IsLiability flag SwitchAccountAsync already correctly uses —
        // catches any liability category regardless of what it's named
        // ("Home Loan", "Car EMI", "Visa Card", none of which the old
        // string match would have caught).
        bool isLiability = sourceAccount.AccountCategory?.IsLiability ?? false;

        if (!isLiability && sourceAccount.Balance < dto.Amount)
        {
            return Result.Failure<bool>(new Error(
                "Transfer.InsufficientFunds",
                $"Insufficient balance. Available: {sourceAccount.Balance:F2}, Requested: {dto.Amount:F2}."));
        }

        if (await IsRapidDuplicateAsync(userId, sourceAccountId, dto.Amount, TransactionType.Expense, dto.Date,
                $"Transfer to {destinationAccount.Name}", true))
            return Result.Failure<bool>(new Error("Transfer.DuplicateSubmit",
                "That exact transfer was just saved. If you meant to do it twice, wait a few seconds and try again."));

        // ── Create the two transaction legs, linked by a shared TransferGroupId ──
        var transferGroupId = Guid.NewGuid();

        var expenseTransaction = new Transaction
        {
            Description           = $"Transfer to {destinationAccount.Name}",
            Amount                = dto.Amount,
            Date                  = dto.Date.EnsureUtc(),
            Type                  = TransactionType.Expense,
            AccountId             = sourceAccountId,
            TransactionCategoryId = dto.TransactionCategoryId,
            UserId                = userId,
            TransferGroupId       = transferGroupId,
            Kind                  = TransactionKind.Transfer
        };
        sourceAccount.Balance -= dto.Amount;

        var incomeTransaction = new Transaction
        {
            Description           = $"Transfer from {sourceAccount.Name}",
            Amount                = dto.Amount,
            Date                  = dto.Date.EnsureUtc(),
            Type                  = TransactionType.Income,
            AccountId             = dto.DestinationAccountId,
            TransactionCategoryId = dto.TransactionCategoryId,
            UserId                = userId,
            TransferGroupId       = transferGroupId,
            Kind                  = TransactionKind.Transfer
        };
        destinationAccount.Balance += dto.Amount;

        _context.Transactions.Add(expenseTransaction);
        _context.Transactions.Add(incomeTransaction);
        _context.Accounts.Update(sourceAccount);
        _context.Accounts.Update(destinationAccount);

        var saveResult = await ConcurrencySafeSave.TrySaveChangesAsync(_context);
        if (saveResult.IsFailure)
            return Result.Failure<bool>(saveResult.Error);

        await _cache.InvalidateDashboardAsync(userId);

        return Result.Success(true);
    }

    public async Task<Result<bool>> SwitchAccountAsync(string userId, int transactionId, int destinationAccountId)
    {
        var transaction = await _context.Transactions.FindAsync(transactionId);
        if (transaction == null)
            return Result.Failure<bool>(new Error("Transaction.NotFound", "Transaction not found."));

        if (transaction.Kind == TransactionKind.Transfer || transaction.Kind == TransactionKind.LoanPrincipal)
        {
            return Result.Failure<bool>(new Error(
                "Transaction.PartOfPair",
                "This transaction is one side of a transfer or loan payment and can't be deleted on its own — deleting only one leg would leave the accounting inconsistent. Contact support or use the account merge/correction tools if this needs reversing."));
        }

        var sourceAccount = await _context.Accounts
            .Include(a => a.AccountCategory)
            .FirstOrDefaultAsync(a => a.Id == transaction.AccountId);

        var destinationAccount = await _context.Accounts
            .Include(a => a.AccountCategory)
            .FirstOrDefaultAsync(a => a.Id == destinationAccountId);

        if (sourceAccount == null || destinationAccount == null || sourceAccount.UserId != userId || destinationAccount.UserId != userId)
            return Result.Failure<bool>(new Error("Account.NotFound", "One or both accounts could not be found."));

        if (sourceAccount.Id == destinationAccount.Id)
            return Result.Failure<bool>(new Error("Switch.SameAccount", "Cannot switch to the same account."));

        // ── Balance check — only matters when moving an Expense, since that's
        // the case that DEBITS the destination account. Moving an Income only
        // credits the destination, which can never cause a shortfall.
        // Same credit-card/loan exemption as CreateTransferAsync — those
        // accounts are allowed to go negative by design (that's what a
        // liability balance means).
        if (transaction.Type == TransactionType.Expense)
        {
            var destCategoryName = destinationAccount.AccountCategory?.Name ?? string.Empty;
            bool isDestLiability = destinationAccount.AccountCategory?.IsLiability ?? false;

            if (!isDestLiability && destinationAccount.Balance < transaction.Amount)
            {
                return Result.Failure<bool>(new Error(
                    "Switch.InsufficientFunds",
                    $"Destination account has insufficient balance. Available: {destinationAccount.Balance:F2}, Required: {transaction.Amount:F2}."));
            }
        }

        // Revert from source
        sourceAccount.Balance += (transaction.Type == TransactionType.Income ? -transaction.Amount : transaction.Amount);

        // Apply to destination
        destinationAccount.Balance += (transaction.Type == TransactionType.Income ? transaction.Amount : -transaction.Amount);

        // Move transaction
        transaction.AccountId = destinationAccountId;

        _context.Accounts.Update(sourceAccount);
        _context.Accounts.Update(destinationAccount);
        _context.Transactions.Update(transaction);

        var saveResult = await ConcurrencySafeSave.TrySaveChangesAsync(_context);
        if (saveResult.IsFailure)
            return Result.Failure<bool>(saveResult.Error);

        await _cache.InvalidateDashboardAsync(userId);

        return Result.Success(true);
    }

    public async Task<Result<TransactionPageResultDto>> GetAllTransactionsAsync(string userId, QueryParameters queryParams)
    {
        // Get all account IDs for this user
        var userAccountIds = await _context.Accounts
            .Where(a => a.UserId == userId)
            .Select(a => a.Id)
            .ToListAsync();

        if (!userAccountIds.Any())
        {
            return Result.Success(new TransactionPageResultDto
            {
                Transactions = new PaginatedResult<TransactionDto>(new List<TransactionDto>(), 0, 1, queryParams.PageSize),
                AvailableCategories = new List<string>()
            });
        }

        // Parse month/year from Filters, default to current month/year
        var now = DateTime.UtcNow;
        int month = now.Month;
        int year = now.Year;

        if (queryParams.Filters.TryGetValue("month", out var monthStr) && int.TryParse(monthStr, out var parsedMonth))
            month = parsedMonth;
        if (queryParams.Filters.TryGetValue("year", out var yearStr) && int.TryParse(yearStr, out var parsedYear))
            year = parsedYear;

        var (startOfMonth, endOfMonth) = AppTimeZone.MonthBoundsUtc(
            new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Unspecified).ToUtc());

        // Base queryable: all transactions across user's accounts
        var allTransactions = _context.Transactions
            .Where(t => userAccountIds.Contains(t.AccountId))
            .Include(t => t.TransactionCategory)
            .Include(t => t.Account);

        // --- Available categories ---
        var availableCategories = await _context.TransactionCategories
            .Where(tc => tc.UserId == userId)
            .Select(tc => tc.Name)
            .Distinct()
            .ToListAsync();

        if (!availableCategories.Contains("Transfer"))
        {
            availableCategories.Add("Transfer");
        }
        
        availableCategories = availableCategories.OrderBy(name => name).ToList();

        // --- Balance Brought Forward / Closing: REAL spendable cash (bank, cash and accounts marked
        // spendable) at the start / end of the month. Current balance minus the movements after that
        // point, so it matches statements. Card and loan movements are not cash and are excluded.
        var spendableAccountIds = await _context.Accounts
            .Where(a => a.UserId == userId)
            .Where(CashRules.IsSpendable)
            .Select(a => a.Id)
            .ToListAsync();
        var spendableNow = await _context.Accounts
            .Where(a => spendableAccountIds.Contains(a.Id))
            .SumAsync(a => a.Balance);
        var movementSinceStart = await _context.Transactions
            .Where(t => spendableAccountIds.Contains(t.AccountId) && t.Date >= startOfMonth)
            .SumAsync(t => t.Type == TransactionType.Income ? t.Amount : -t.Amount);
        var movementAfterEnd = await _context.Transactions
            .Where(t => spendableAccountIds.Contains(t.AccountId) && t.Date > endOfMonth)
            .SumAsync(t => t.Type == TransactionType.Income ? t.Amount : -t.Amount);
        var balanceBF = spendableNow - movementSinceStart;
        var closingBalance = spendableNow - movementAfterEnd;

        // --- Monthly totals (respects category filter for consistency with table) ---
        var monthlyQuery = allTransactions
            .Where(t => t.Date >= startOfMonth && t.Date <= endOfMonth);

        if (queryParams.Filters.TryGetValue("categoryName", out var categoryName) && !string.IsNullOrWhiteSpace(categoryName))
        {
            monthlyQuery = monthlyQuery.Where(t =>
                t.TransactionCategory != null && t.TransactionCategory.Name == categoryName);
        }

        if (queryParams.Filters.TryGetValue("tag", out var tagFilter2) && !string.IsNullOrWhiteSpace(tagFilter2))
        {
            var like2 = TransactionTags.Pattern(tagFilter2);
            monthlyQuery = monthlyQuery.Where(t => t.Tags != null && EF.Functions.Like(t.Tags, like2, "\\"));
        }

        var totalIncome = await monthlyQuery
            .Where(t => t.Type == TransactionType.Income && t.TransferGroupId == null)
            .SumAsync(t => t.Amount);

        var totalExpenses = await monthlyQuery
            .Where(t => t.Type == TransactionType.Expense && t.TransferGroupId == null)
            .SumAsync(t => t.Amount);

        // --- Filtered queryable for the transaction list ---
        var filteredQuery = monthlyQuery; // Reuse the filtered monthly query

        // Global search (applied only to the list, not the high-level monthly summaries)
        if (!string.IsNullOrWhiteSpace(queryParams.GlobalSearch))
        {
            var search = queryParams.GlobalSearch.Trim().ToLower();
            filteredQuery = filteredQuery.Where(t =>
                t.Description.ToLower().Contains(search) ||
                (t.TransactionCategory != null && t.TransactionCategory.Name.ToLower().Contains(search)) ||
                (t.Notes != null && t.Notes.ToLower().Contains(search)) ||
                (t.Tags != null && t.Tags.Contains(search)) ||
                t.Account.Name.ToLower().Contains(search));
        }

        // Newest first, continued by cursor: no OFFSET, and no COUNT after the first page.
        var seeking = KeysetPager.TryDecodeCursor<DateTime>(queryParams.Cursor, out _, out _);
        var totalRecords = seeking ? -1 : await filteredQuery.CountAsync();

        var keysetPage = await filteredQuery.ToKeysetPageAsync(t => t.Date, t => t.Id, true, true, queryParams.Cursor, queryParams.PageSize,
            include: q => q.Include(t => t.TransactionCategory).Include(t => t.Account).Include(t => t.Merchant),
            skip: (queryParams.PageNumber - 1) * queryParams.PageSize);

        var dtos = keysetPage.Items.Select(MapToDto).ToList();

        var result = new TransactionPageResultDto
        {
            Transactions = new PaginatedResult<TransactionDto>(dtos, totalRecords, queryParams.PageNumber, queryParams.PageSize) { HasMore = keysetPage.HasMore, NextCursor = keysetPage.NextCursor },
            BalanceBroughtForward = balanceBF,
            ClosingBalance = closingBalance,
            TotalIncome = totalIncome,
            TotalExpenses = totalExpenses,
            TotalSavings = totalIncome - totalExpenses,
            AvailableCategories = availableCategories
        };

        return Result.Success(result);
    }

    private TransactionDto MapToDto(Transaction t)
    {
        string? categoryName = t.TransactionCategory?.Name;
        if (string.IsNullOrEmpty(categoryName))
        {
            var desc = t.Description ?? string.Empty;
            if (t.Kind == TransactionKind.Transfer ||
                desc.Contains("Transfer", StringComparison.OrdinalIgnoreCase) ||
                desc.Contains("Credit card payment", StringComparison.OrdinalIgnoreCase) ||
                desc.Contains("Payment from", StringComparison.OrdinalIgnoreCase) ||
                desc.Contains("Loan principal", StringComparison.OrdinalIgnoreCase))
            {
                categoryName = "Transfer";
            }
            else if (t.Kind == TransactionKind.LoanInterest ||
                     desc.StartsWith("Interest", StringComparison.OrdinalIgnoreCase) ||
                     desc.Contains("Interest/fees", StringComparison.OrdinalIgnoreCase))
            {
                categoryName = "Interest & Fees";
            }
            else if (desc.StartsWith("Cashback", StringComparison.OrdinalIgnoreCase))
            {
                categoryName = "Cashback & Rewards";
            }
            else if (t.IsOpeningBalance || t.Kind == TransactionKind.OpeningBalance || desc.Contains("Opening Balance", StringComparison.OrdinalIgnoreCase))
            {
                categoryName = "Opening Balance";
            }
            else if (t.IsBalanceAdjustment || t.Kind == TransactionKind.BalanceAdjustment || desc.Contains("Balance Adjustment", StringComparison.OrdinalIgnoreCase))
            {
                categoryName = "Balance Adjustment";
            }
        }

        return new TransactionDto
        {
            Id = t.Id,
            UserId = t.UserId,
            Description = t.Description,
            Amount = t.Amount,
            Date = t.Date,
            Type = t.Type,
            AccountId = t.AccountId,
            AccountName = t.Account?.Name,
            CategoryName = categoryName,
            MerchantId = t.MerchantId,
            MerchantName = t.Merchant?.Name,
            Notes = t.Notes,
            Tags = TransactionTags.Unpack(t.Tags),
            TransferGroupId = t.TransferGroupId
        };
    }

    private string ToTitleCase(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return input;
        return System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(input.ToLower());
    }

    /// <summary>
    /// After a NEW expense is saved: finds budgets it has just pushed past their alert level (user default or
    /// per-budget override) or past 100 %, logs a BudgetAlertEvent for each (feeds behaviour reports), emails only
    /// if the user opted in under Budget settings, and returns the alerts so the UI can toast immediately.
    /// </summary>
    private async Task<List<BudgetAlertDto>> CheckBudgetAlertsAsync(string userId, Transaction transaction)
    {
        var alerts = new List<BudgetAlertDto>();
        if (transaction.TransactionCategoryId is null)
            return alerts; // no category, nothing to match against a category budget

        var rollupMap = await CategoryRollup.BuildRollupMapAsync(_context, userId);

        var candidateBudgets = await _context.Budgets
            .Where(b => b.UserId == userId && !b.IsDeleted &&
                        b.StartDate <= transaction.Date &&
                        (b.EndDate == null || b.EndDate >= transaction.Date))
            .ToListAsync();

        var matchingBudgets = candidateBudgets
            .Where(b => CategoryRollup.Matches(rollupMap, b.TransactionCategoryId, transaction.TransactionCategoryId))
            .ToList();

        if (!matchingBudgets.Any())
            return alerts;

        var general = await _userSettings.GetGeneralAsync(userId);
        var budgetSettings = await _userSettings.GetBudgetSettingsAsync(userId);

        var accountIds = await _context.Accounts
            .Where(a => a.UserId == userId && !a.IsDeleted)
            .Select(a => a.Id)
            .ToListAsync();

        string? userEmail = null;
        var emailLookedUp = false;

        foreach (var budget in matchingBudgets)
        {
            var (periodStart, periodEnd) = budget.Period switch
            {
                Domain.Enums.BudgetPeriod.Weekly => AppTimeZone.WeekBoundsUtc(transaction.Date),
                Domain.Enums.BudgetPeriod.Yearly => AppTimeZone.YearBoundsUtc(transaction.Date),
                _ => AppTimeZone.MonthBoundsUtc(transaction.Date, general.MonthStartDay) // Monthly, and the default
            };

            var budgetCategoryIds = budget.TransactionCategoryId.HasValue
                && rollupMap.TryGetValue(budget.TransactionCategoryId.Value, out var mapped)
                    ? mapped
                    : new List<int>();

            var spentAfter = await _context.Transactions
                .Where(t => accountIds.Contains(t.AccountId) && !t.IsDeleted &&
                            t.Type == TransactionType.Expense &&
                            t.TransferGroupId == null &&
                            t.Date >= periodStart && t.Date <= periodEnd &&
                            (budget.TransactionCategoryId == null ||
                             (t.TransactionCategoryId.HasValue && budgetCategoryIds.Contains(t.TransactionCategoryId.Value))))
                .SumAsync(t => t.Amount);

            // transaction.Amount is already included in spentAfter, since it
            // was just committed via SaveChangesAsync before this method runs.
            var spentBefore = spentAfter - transaction.Amount;

            var threshold = budget.AlertThresholdPercent ?? budgetSettings.DefaultAlertPercent;
            var warnAt = budget.Amount * threshold / 100m;
            bool justCrossedLimit = spentBefore < budget.Amount && spentAfter >= budget.Amount;
            // Only the transaction that takes spend over the alert level fires it; a jump straight past 100 % fires the limit alert only.
            bool justCrossedWarning = !justCrossedLimit && budget.Amount > 0 && spentBefore < warnAt && spentAfter >= warnAt && spentAfter < budget.Amount;
            if (!justCrossedLimit && !justCrossedWarning)
                continue; // already past the line before this transaction, or still under — no alert either way

            var categoryName = budget.TransactionCategoryId.HasValue
                ? (await _context.TransactionCategories.FindAsync(budget.TransactionCategoryId.Value))?.Name ?? "this category"
                : "your overall budget";
            var percentUsed = budget.Amount > 0 ? Math.Round(spentAfter / budget.Amount * 100, 2) : 0m;
            var level = justCrossedLimit ? 2 : 1;

            _context.BudgetAlertEvents.Add(new BudgetAlertEvent
            {
                UserId = userId,
                BudgetId = budget.Id,
                TransactionId = transaction.Id,
                Level = level,
                ThresholdPercent = threshold,
                PercentUsed = percentUsed,
                SpentAmount = spentAfter,
                BudgetAmount = budget.Amount,
                CategoryName = categoryName
            });

            if (budgetSettings.ToastAlerts)
            {
                alerts.Add(new BudgetAlertDto
                {
                    BudgetId = budget.Id,
                    CategoryName = categoryName,
                    Level = level,
                    ThresholdPercent = threshold,
                    PercentUsed = percentUsed,
                    SpentAmount = spentAfter,
                    BudgetAmount = budget.Amount
                });
            }

            if (!budgetSettings.EmailAlerts)
                continue; // email is opt-in (off by default)

            if (!emailLookedUp)
            {
                userEmail = await _context.Users.Where(u => u.Id == userId).Select(u => u.Email).FirstOrDefaultAsync();
                emailLookedUp = true;
            }
            if (string.IsNullOrEmpty(userEmail))
                continue;

            var safeName = System.Net.WebUtility.HtmlEncode(categoryName);
            await _emailService.SendEmailAsync(justCrossedLimit
                ? new MailRequest
                {
                    To = userEmail,
                    Subject = $"Budget Alert: {categoryName} limit reached",
                    Body = $"<p>You've just crossed your {budget.Period} budget of ₹{budget.Amount:F2} for <strong>{safeName}</strong>.</p>" +
                           $"<p>Total spent this period: ₹{spentAfter:F2}</p>"
                }
                : new MailRequest
                {
                    To = userEmail,
                    Subject = $"Budget heads-up: {categoryName} is at {threshold}%",
                    Body = $"<p>You've used {percentUsed:0}% of your {budget.Period} budget of ₹{budget.Amount:F2} for <strong>{safeName}</strong> (your alert level is {threshold}%).</p>" +
                           $"<p>Spent so far: ₹{spentAfter:F2} · left: ₹{(budget.Amount - spentAfter):F2}</p>"
                });

            _logger.LogInformation(
                "Budget alert emailed to {UserId} for category {CategoryId} — budget {BudgetAmount}, spent {SpentAmount}.",
                userId, budget.TransactionCategoryId, budget.Amount, spentAfter);
        }

        await _context.SaveChangesAsync(); // persists the alert events
        return alerts;
    }
}
