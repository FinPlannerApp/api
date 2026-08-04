namespace Application.Services;


using Application.Common.Helpers;
using Application.Common.Models;
using Application.Contracts;
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

    public TransactionService(
        IApplicationDbContext context,
        ICacheService cache,
        ILogger<TransactionService> logger)
    {
        _context = context;
        _cache = cache;
        _logger = logger;
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

        // Category Filter
        if (queryParams.Filters.TryGetValue("categoryName", out var categoryName) && !string.IsNullOrWhiteSpace(categoryName))
        {
            queryable = queryable.Where(t => t.TransactionCategory != null && t.TransactionCategory.Name == categoryName);
        }

        if (!string.IsNullOrWhiteSpace(queryParams.GlobalSearch))
        {
            var search = queryParams.GlobalSearch.Trim().ToLower();
            queryable = queryable.Where(t => t.Description.ToLower().Contains(search) ||
                (t.TransactionCategory != null && t.TransactionCategory.Name.ToLower().Contains(search)));
        }

        if (queryParams.TransactionCategoryId.HasValue)
        {
            queryable = queryable.Where(t => t.TransactionCategoryId == queryParams.TransactionCategoryId.Value);
        }

        var totalRecords = await queryable.CountAsync();

        // Apply dynamic sorting
        queryable = queryParams.SortBy?.ToLower() switch
        {
            "date" => queryParams.SortOrder == "asc" ? queryable.OrderBy(t => t.Date).ThenByDescending(t => t.Id) : queryable.OrderByDescending(t => t.Date).ThenByDescending(t => t.Id),
            "amount" => queryParams.SortOrder == "desc" ? queryable.OrderByDescending(t => t.Amount).ThenByDescending(t => t.Id) : queryable.OrderBy(t => t.Amount).ThenByDescending(t => t.Id),
            "category" or "categoryname" => queryParams.SortOrder == "desc" ? queryable.OrderByDescending(t => t.TransactionCategory != null ? t.TransactionCategory.Name.ToLower() : "").ThenByDescending(t => t.Id) : queryable.OrderBy(t => t.TransactionCategory != null ? t.TransactionCategory.Name.ToLower() : "").ThenByDescending(t => t.Id),
            "description" => queryParams.SortOrder == "desc" ? queryable.OrderByDescending(t => t.Description.ToLower()).ThenByDescending(t => t.Id) : queryable.OrderBy(t => t.Description.ToLower()).ThenByDescending(t => t.Id),
            _ => queryable.OrderByDescending(t => t.Date).ThenByDescending(t => t.Id) // Default
        };

        var items = await queryable
            .Include(t => t.TransactionCategory)
            .Include(t => t.Account)
            .Skip((queryParams.PageNumber - 1) * queryParams.PageSize)
            .Take(queryParams.PageSize)
            .ToListAsync();

        var pagedDto = new PaginatedResult<TransactionDto>(
            items.Select(MapToDto).ToList(),
            totalRecords,
            queryParams.PageNumber,
            queryParams.PageSize
        );
        return Result.Success(pagedDto);
    }

    public async Task<Result<BulkInsertResponseDto>> BulkUpsertTransactionsAsync(string userId, BulkTransactionPayloadDto payload)
    {
        var response = new BulkInsertResponseDto();

        using var dbTransaction = await ((DbContext)_context).Database.BeginTransactionAsync();

        try
        {
            for (int i = 0; i < payload.Transactions.Count; i++)
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
                await dbTransaction.CommitAsync();
                await _cache.RemoveByPrefixAsync($"dash::{userId}:");
            }
            else
            {
                // Roll back everything — no partial financial state
                await dbTransaction.RollbackAsync();
                response.SuccessfulCount = 0; // none were actually committed
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


    public async Task<Result<TransactionDto>> UpsertTransactionAsync(string userId, int accountId, UpsertTransactionDto dto)
    {
        if (!string.IsNullOrWhiteSpace(dto.Description))
            dto.Description = ToTitleCase(dto.Description);

        var account = await _context.Accounts.FindAsync(accountId);
        if (account == null || account.UserId != userId)
            return Result.Failure<TransactionDto>(new Error("Account.NotFound", "Account not found."));

        Transaction? transaction = null;
        decimal oldAmount = 0;
        TransactionType oldType = TransactionType.Expense;

        if (dto.Id.HasValue && dto.Id.Value > 0)
        {
            transaction = await _context.Transactions.FindAsync(dto.Id.Value);
            if (transaction == null || transaction.AccountId != accountId)
                return Result.Failure<TransactionDto>(new Error("Transaction.NotFound", "Transaction not found."));

            oldAmount = transaction.Amount;
            oldType = transaction.Type;
            transaction.Description = dto.Description;
            transaction.Amount = dto.Amount;
            transaction.Date = dto.Date;
            transaction.Type = dto.Type;
            transaction.TransactionCategoryId = dto.TransactionCategoryId;
            _context.Transactions.Update(transaction);
        }
        else
        {
            transaction = new Transaction
            {
                Description = dto.Description,
                Amount = dto.Amount,
                Date = dto.Date,
                Type = dto.Type,
                TransactionCategoryId = dto.TransactionCategoryId,
                AccountId = accountId,
                UserId = userId
            };
            _context.Transactions.Add(transaction);
        }

        // Adjust Account Balance
        account.Balance += (oldType == TransactionType.Income ? -oldAmount : oldAmount);
        account.Balance += (transaction.Type == TransactionType.Income ? transaction.Amount : -transaction.Amount);

        _context.Accounts.Update(account);
        
        await _context.SaveChangesAsync();

        // Reload to include relations for return DTO
        var resultTransactionWithCategory = await _context.Transactions
            .Include(t => t.TransactionCategory)
            .FirstAsync(t => t.Id == transaction.Id);

        var resultDto = MapToDto(resultTransactionWithCategory);
        await _cache.RemoveByPrefixAsync($"dash::{userId}:"); // invalidates summary, category, insights

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

        // Revert Balance
        account.Balance += (transaction.Type == TransactionType.Income ? -transaction.Amount : transaction.Amount);

        _context.Accounts.Update(account);
        
        transaction.IsDeleted = true;
        transaction.DeletedAt = DateTime.UtcNow;
        _context.Transactions.Update(transaction);

        await _context.SaveChangesAsync();
        await _cache.RemoveByPrefixAsync($"dash::{userId}:");

        return Result.Success(true);
    }

    public async Task<Result<bool>> CreateTransferAsync(string userId, int sourceAccountId, CreateTransferDto dto)
    {
        // ── Load source account ────────────────────────────────────────────────
        var sourceAccount = await _context.Accounts
            .Include(a => a.AccountCategory)          // needed for credit/loan check
            .FirstOrDefaultAsync(a => a.Id == sourceAccountId);

        if (sourceAccount == null || sourceAccount.UserId != userId)
            return Result.Failure<bool>(new Error("Account.NotFound", "Source account not found."));

        var destinationAccount = await _context.Accounts.FindAsync(dto.DestinationAccountId);
        if (destinationAccount == null || destinationAccount.UserId != userId)
            return Result.Failure<bool>(new Error("Account.NotFound", "Destination account not found."));

        if (sourceAccountId == dto.DestinationAccountId)
            return Result.Failure<bool>(new Error("Transfer.SameAccount", "Cannot transfer to the same account."));

        // ── Balance check (skip for Credit Card / Loan accounts) ──────────────
        var categoryName = sourceAccount.AccountCategory?.Name ?? string.Empty;
        bool isCreditLiability = categoryName.Equals("Credit Card", StringComparison.OrdinalIgnoreCase)
                              || categoryName.Equals("Loan",         StringComparison.OrdinalIgnoreCase)
                              || categoryName.Equals("Credit",       StringComparison.OrdinalIgnoreCase);

        if (!isCreditLiability && sourceAccount.Balance < dto.Amount)
        {
            return Result.Failure<bool>(new Error(
                "Transfer.InsufficientFunds",
                $"Insufficient balance. Available: {sourceAccount.Balance:F2}, Requested: {dto.Amount:F2}."));
        }

        // ── Create the two transaction legs ───────────────────────────────────
        var expenseTransaction = new Transaction
        {
            Description           = $"Transfer to {destinationAccount.Name}",
            Amount                = dto.Amount,
            Date                  = dto.Date,
            Type                  = TransactionType.Expense,
            AccountId             = sourceAccountId,
            TransactionCategoryId = dto.TransactionCategoryId,
            UserId                = userId
        };
        sourceAccount.Balance -= dto.Amount;

        var incomeTransaction = new Transaction
        {
            Description           = $"Transfer from {sourceAccount.Name}",
            Amount                = dto.Amount,
            Date                  = dto.Date,
            Type                  = TransactionType.Income,
            AccountId             = dto.DestinationAccountId,
            TransactionCategoryId = dto.TransactionCategoryId,
            UserId                = userId
        };
        destinationAccount.Balance += dto.Amount;

        _context.Transactions.Add(expenseTransaction);
        _context.Transactions.Add(incomeTransaction);
        _context.Accounts.Update(sourceAccount);
        _context.Accounts.Update(destinationAccount);

        await _context.SaveChangesAsync();
        await _cache.RemoveByPrefixAsync($"dash::{userId}:");

        return Result.Success(true);
    }

    public async Task<Result<bool>> SwitchAccountAsync(string userId, int transactionId, int destinationAccountId)
    {
        var transaction = await _context.Transactions.FindAsync(transactionId);
        if (transaction == null)
            return Result.Failure<bool>(new Error("Transaction.NotFound", "Transaction not found."));

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

        await _context.SaveChangesAsync();
        await _cache.RemoveByPrefixAsync($"dash::{userId}:");

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

        var startOfMonth = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc);
        var endOfMonth = startOfMonth.AddMonths(1).AddTicks(-1);

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

        // --- Balance Brought Forward: net of all transactions BEFORE the selected month ---
        var bfIncome = await _context.Transactions
            .Where(t => userAccountIds.Contains(t.AccountId) && t.Date < startOfMonth && t.Type == TransactionType.Income)
            .SumAsync(t => t.Amount);

        var bfExpenses = await _context.Transactions
            .Where(t => userAccountIds.Contains(t.AccountId) && t.Date < startOfMonth && t.Type == TransactionType.Expense)
            .SumAsync(t => t.Amount);

        var balanceBF = bfIncome - bfExpenses;

        // --- Monthly totals (respects category filter for consistency with table) ---
        var monthlyQuery = allTransactions
            .Where(t => t.Date >= startOfMonth && t.Date <= endOfMonth);

        if (queryParams.Filters.TryGetValue("categoryName", out var categoryName) && !string.IsNullOrWhiteSpace(categoryName))
        {
            monthlyQuery = monthlyQuery.Where(t =>
                t.TransactionCategory != null && t.TransactionCategory.Name == categoryName);
        }

        var totalIncome = await monthlyQuery
            .Where(t => t.Type == TransactionType.Income)
            .SumAsync(t => t.Amount);

        var totalExpenses = await monthlyQuery
            .Where(t => t.Type == TransactionType.Expense)
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
                t.Account.Name.ToLower().Contains(search));
        }

        var totalRecords = await filteredQuery.CountAsync();

        var items = await filteredQuery
            .Include(t => t.TransactionCategory)
            .Include(t => t.Account)
            .OrderByDescending(t => t.Date)
            .ThenByDescending(t => t.Id)
            .Skip((queryParams.PageNumber - 1) * queryParams.PageSize)
            .Take(queryParams.PageSize)
            .ToListAsync();

        var dtos = items.Select(MapToDto).ToList();

        var result = new TransactionPageResultDto
        {
            Transactions = new PaginatedResult<TransactionDto>(dtos, totalRecords, queryParams.PageNumber, queryParams.PageSize),
            BalanceBroughtForward = balanceBF,
            TotalIncome = totalIncome,
            TotalExpenses = totalExpenses,
            TotalSavings = totalIncome - totalExpenses,
            AvailableCategories = availableCategories
        };

        return Result.Success(result);
    }

    private TransactionDto MapToDto(Transaction t)
    {
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
            CategoryName = t.TransactionCategory?.Name ?? (t.Description.Contains("Transfer") ? "Transfer" : null)
        };
    }

    private string ToTitleCase(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return input;
        return System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(input.ToLower());
    }
}