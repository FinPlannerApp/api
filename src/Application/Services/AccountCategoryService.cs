
using Application.Common.Models;
using Application.Contracts;
using Application.DTOs.AccountCategory;
using Application.DTOs.Categories;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Application.Services;

public class AccountCategoryService : ICategoryService<AccountCategoryDto, UpsertAccountCategoryDto>
{
    private readonly IApplicationDbContext _context;
    private readonly ICacheService _cache;

    private static string AllKey(string userId) => $"ac:all:{userId}";
    public AccountCategoryService(IApplicationDbContext context, ICacheService cache)
    {
        _context = context;
        _cache = cache;
    }

    public async Task<Result<PaginatedResult<AccountCategoryDto>>> GetPagedAsync(string userId, QueryParameters queryParams)
    {
        var query = _context.AccountCategories
            .Where(c => c.UserId == userId && !c.IsDeleted)
            .AsQueryable();

        // Apply global search
        if (!string.IsNullOrWhiteSpace(queryParams.GlobalSearch))
        {
            query = query.Where(c => c.Name.ToLower().Contains(queryParams.GlobalSearch.ToLower()));
        }

        // Get total count before pagination
        var totalRecords = await query.CountAsync();

        // Apply sorting
        query = queryParams.SortBy?.ToLower() switch
        {
            "name" => queryParams.SortOrder == "desc" ? query.OrderByDescending(c => c.Name) : query.OrderBy(c => c.Name),
            _ => query.OrderBy(c => c.Name)
        };

        // Apply pagination
        var items = await query
            .Skip((queryParams.PageNumber - 1) * queryParams.PageSize)
            .Take(queryParams.PageSize)
            .ToListAsync();

        var mapped = items.Select(MapToDto).ToList();
        var result = new PaginatedResult<AccountCategoryDto>(mapped, totalRecords, queryParams.PageNumber, queryParams.PageSize);
        return Result.Success(result);
    }

    public async Task<Result<IReadOnlyList<AccountCategoryDto>>> GetAllAsync(string userId)
    {
        var cacheKey = AllKey(userId);
        var cached = await _cache.GetAsync<IReadOnlyList<AccountCategoryDto>>(cacheKey);
        if (cached is not null)
            return Result.Success(cached);

        var allCategories = await _context.AccountCategories
            .Where(c => c.UserId == userId && !c.IsDeleted)
            .ToListAsync();

        var mapped = allCategories.Select(MapToDto).ToList();
        await _cache.SetAsync(cacheKey, mapped, TimeSpan.FromHours(1));
        return Result.Success<IReadOnlyList<AccountCategoryDto>>(mapped);
    }

    public async Task<Result<AccountCategoryDto>> UpsertAsync(string userId, UpsertAccountCategoryDto dto)
    {
        AccountCategory? category = null;

        if (dto.Id.HasValue && dto.Id > 0)
        {
            category = await _context.AccountCategories.FindAsync(dto.Id.Value);

            if (category == null || category.UserId != userId)
                return Result.Failure<AccountCategoryDto>(new Error("Category.NotFound", "Category not found."));

            category.Name        = ToTitleCase(dto.Name);
            category.IsLiability = dto.IsLiability;
            category.AccountType = dto.AccountType;
            _context.AccountCategories.Update(category);
        }
        else
        {
            category = new AccountCategory
            {
                Name        = ToTitleCase(dto.Name),
                UserId      = userId,
                IsLiability = dto.IsLiability,
                AccountType = dto.AccountType
            };
            _context.AccountCategories.Add(category);
        }

        try
        {
            await _context.SaveChangesAsync();
            await _cache.RemoveAsync(AllKey(userId)); // invalidate stale list
            var resultDto = MapToDto(category);
            return Result.Success(resultDto);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException postgresEx && postgresEx.SqlState == "23505")
        {
            return Result.Failure<AccountCategoryDto>(new Error("Category.Duplicate", "A category with this name already exists."));
        }
    }

    public async Task<Result<bool>> DeleteAsync(string userId, int id)
    {
        var category = await _context.AccountCategories.FindAsync(id);
        if (category == null || category.UserId != userId)
            return Result.Failure<bool>(new Error("Category.NotFound", "Category not found."));

        category.IsDeleted = true;
        category.DeletedAt = DateTime.UtcNow;
        _context.AccountCategories.Update(category);
        await _context.SaveChangesAsync();
        await _cache.RemoveAsync(AllKey(userId));
        return Result.Success(true);
    }

    public async Task<Result<bool>> MergeAsync(string userId, MergeAccountCategoriesDto dto)
    {
        if (dto.SourceCategoryId == dto.TargetCategoryId)
            return Result.Failure<bool>(new Error("Category.SameCategory", "Cannot merge a category into itself."));

        var source = await _context.AccountCategories.FindAsync(dto.SourceCategoryId);
        var target = await _context.AccountCategories.FindAsync(dto.TargetCategoryId);

        if (source == null || source.UserId != userId || target == null || target.UserId != userId)
            return Result.Failure<bool>(new Error("Category.NotFound", "One or both categories could not be found."));

        // Reassign every account currently using the source category —
        // this is the actual merge. Nothing about the accounts themselves
        // changes except which category they point at.
        var affectedAccounts = await _context.Accounts
            .Where(a => a.AccountCategoryId == dto.SourceCategoryId && a.UserId == userId)
            .ToListAsync();

        foreach (var account in affectedAccounts)
        {
            account.AccountCategoryId = dto.TargetCategoryId;
        }
        _context.Accounts.UpdateRange(affectedAccounts);

        // Retire the source category — soft delete, same convention as
        // everything else in this app, not a hard delete.
        source.IsDeleted = true;
        source.DeletedAt = DateTime.UtcNow;
        _context.AccountCategories.Update(source);

        await _context.SaveChangesAsync();
        await _cache.RemoveAsync(AllKey(userId)); // invalidate the cached category list
        await _cache.RemoveByPrefixAsync($"dash::{userId}:"); // net worth/summary may reference category-derived data

        return Result.Success(true);
    }

    private AccountCategoryDto MapToDto(AccountCategory c)
    {
        return new AccountCategoryDto
        {
            Id          = c.Id,
            Name        = c.Name,
            IsLiability = c.IsLiability,
            AccountType = c.AccountType
        };
    }

    private string ToTitleCase(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return input;
        return System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(input.ToLower());
    }
}