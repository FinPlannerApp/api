
using Application.Common.Models;
using Application.Contracts;
using Application.DTOs.Categories;
using Application.DTOs.TransactionCategory;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Application.Services;

public class TransactionCategoryService : ICategoryService<TransactionCategoryDto, UpsertTransactionCategoryDto>
{
    private readonly IApplicationDbContext _context;
    private readonly ICacheService _cache;

    private static string AllKey(string userId) => $"tc:all:{userId}";
    public TransactionCategoryService(IApplicationDbContext context, ICacheService cache)
    {
        _context = context;
        _cache = cache;
    }

    public async Task<Result<PaginatedResult<TransactionCategoryDto>>> GetPagedAsync(string userId, QueryParameters queryParams)
    {
        var query = _context.TransactionCategories
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
        var result = new PaginatedResult<TransactionCategoryDto>(mapped, totalRecords, queryParams.PageNumber, queryParams.PageSize);
        return Result.Success(result);
    }

    public async Task<Result<IReadOnlyList<TransactionCategoryDto>>> GetAllAsync(string userId)
    {
        var cacheKey = AllKey(userId);
        var cached = await _cache.GetAsync<IReadOnlyList<TransactionCategoryDto>>(cacheKey);
        if (cached is not null)
            return Result.Success(cached);

        var allCategories = await _context.TransactionCategories
            .Where(c => c.UserId == userId && !c.IsDeleted)
            .ToListAsync();

        var mapped = allCategories.Select(MapToDto).ToList();
        await _cache.SetAsync(cacheKey, mapped, TimeSpan.FromHours(1));
        return Result.Success<IReadOnlyList<TransactionCategoryDto>>(mapped);
    }

    public async Task<Result<TransactionCategoryDto>> UpsertAsync(string userId, UpsertTransactionCategoryDto dto)
    {
        TransactionCategory? category = null;

        if (dto.Id.HasValue && dto.Id > 0)
        {
            category = await _context.TransactionCategories.FindAsync(dto.Id.Value);

            if (category == null || category.UserId != userId)
                return Result.Failure<TransactionCategoryDto>(new Error("Category.NotFound", "Category not found."));

            category.Name = ToTitleCase(dto.Name);
            _context.TransactionCategories.Update(category);
        }
        else
        {
            category = new TransactionCategory
            {
                Name = ToTitleCase(dto.Name),
                UserId = userId
            };
            _context.TransactionCategories.Add(category);
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
            return Result.Failure<TransactionCategoryDto>(new Error("Category.Duplicate", "A category with this name already exists."));
        }
    }

    public async Task<Result<bool>> DeleteAsync(string userId, int id)
    {
        var category = await _context.TransactionCategories.FindAsync(id);
        if (category == null || category.UserId != userId)
            return Result.Failure<bool>(new Error("Category.NotFound", "Category not found."));

        category.IsDeleted = true;
        category.DeletedAt = DateTime.UtcNow;
        _context.TransactionCategories.Update(category);
        await _context.SaveChangesAsync();
        await _cache.RemoveAsync(AllKey(userId));
        return Result.Success(true);
    }

    private TransactionCategoryDto MapToDto(TransactionCategory c)
    {
        return new TransactionCategoryDto
        {
            Id = c.Id,
            Name = c.Name,
            IsTransferCategory = c.IsTransferCategory
        };
    }

    private string ToTitleCase(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return input;
        return System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(input.ToLower());
    }
}