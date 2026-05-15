
using Application.Common.Models;
using Application.Contracts;
using Application.DTOs.Accounts;
using Domain.Entities;
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

        // Apply sorting
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
            items.Select(MapToDto).ToList(),
            totalRecords,
            queryParams.PageNumber,
            queryParams.PageSize
        );
        return Result.Success(pagedDto);
    }

    public async Task<Result<List<AccountDto>>> GetAllAccountsAsync(string userId)
    {
        var items = await _context.Accounts
            .Where(a => a.UserId == userId && !a.IsDeleted)
            .OrderBy(a => a.Name)
            .ToListAsync();

        return Result.Success(items.Select(MapToDto).ToList());
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

        if (dto.Id.HasValue && dto.Id > 0)
        {
            // Edit Mode
            account = await _context.Accounts
                .Include(a => a.AccountCategory)
                .FirstOrDefaultAsync(a => a.Id == dto.Id);

            if (account == null || account.UserId != userId)
                return Result.Failure<AccountDto>(new Error("Account.NotFound", "Account not found."));

            account.Name = dto.Name;
            account.Balance = dto.Balance;
            account.AccountCategoryId = dto.AccountCategoryId;
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
                UserId = userId
            };
            _context.Accounts.Add(account);
        }

        await _context.SaveChangesAsync();

        // Reload to get populated fields (like Category name if needed, or DB generated values)
        // Note: For simple update, memory object is usually enough, but if we need relations:
        // account = await _context.Accounts.Include(a => a.AccountCategory).FirstAsync(a => a.Id == account.Id); 

        var resultDto = MapToDto(account);
        await _cache.RemoveByPrefixAsync($"dash::{userId}:"); // Invalidate net worth
        return Result.Success(resultDto);
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

    private AccountDto MapToDto(Account a)
    {
        return new AccountDto
        {
            Id = a.Id,
            Name = a.Name,
            Balance = a.Balance,
            AccountCategoryName = a.AccountCategory?.Name ?? ""
        };
    }

    private string ToTitleCase(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return input;
        return System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(input.ToLower());
    }
}