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
            account.Balance = dto.Balance;
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
                StartDate = a.LoanDetails.StartDate
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