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
            .Include(c => c.ParentCategory)
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
            .Include(c => c.ParentCategory)
            .Where(c => c.UserId == userId && !c.IsDeleted)
            .ToListAsync();

        var mapped = allCategories.Select(MapToDto).ToList();
        await _cache.SetAsync(cacheKey, mapped, TimeSpan.FromHours(1));
        return Result.Success<IReadOnlyList<TransactionCategoryDto>>(mapped);
    }

    public async Task<Result<TransactionCategoryDto>> UpsertAsync(string userId, UpsertTransactionCategoryDto dto)
    {
        if (dto.ParentCategoryId.HasValue)
        {
            var parent = await _context.TransactionCategories
                .FirstOrDefaultAsync(c => c.Id == dto.ParentCategoryId.Value);

            if (parent == null || parent.UserId != userId)
                return Result.Failure<TransactionCategoryDto>(
                    new Error("Category.ParentNotFound", "Parent category not found."));

            // Enforces the one-level-deep rule: you can't make a subcategory
            // of something that's already a subcategory.
            if (parent.ParentCategoryId.HasValue)
                return Result.Failure<TransactionCategoryDto>(
                    new Error("Category.NestingTooDeep",
                        $"'{parent.Name}' is already a subcategory. Subcategories can only be one level deep."));

            // A category can't be its own parent.
            if (dto.Id.HasValue && dto.Id.Value == dto.ParentCategoryId.Value)
                return Result.Failure<TransactionCategoryDto>(
                    new Error("Category.SelfParent", "A category can't be its own parent."));

            // A category that already HAS subcategories can't itself become
            // a subcategory — that would create two levels by the back door.
            if (dto.Id.HasValue)
            {
                var hasChildren = await _context.TransactionCategories
                    .AnyAsync(c => c.ParentCategoryId == dto.Id.Value);

                if (hasChildren)
                    return Result.Failure<TransactionCategoryDto>(
                        new Error("Category.HasSubCategories",
                            "This category has subcategories of its own, so it can't become a subcategory."));
            }
        }

        TransactionCategory? category = null;

        if (dto.Id.HasValue && dto.Id > 0)
        {
            category = await _context.TransactionCategories.FindAsync(dto.Id.Value);

            if (category == null || category.UserId != userId)
                return Result.Failure<TransactionCategoryDto>(new Error("Category.NotFound", "Category not found."));

            category.Name = ToTitleCase(dto.Name);
            category.ParentCategoryId = dto.ParentCategoryId;
            _context.TransactionCategories.Update(category);
        }
        else
        {
            category = new TransactionCategory
            {
                Name = ToTitleCase(dto.Name),
                UserId = userId,
                ParentCategoryId = dto.ParentCategoryId
            };
            _context.TransactionCategories.Add(category);
        }

        try
        {
            await _context.SaveChangesAsync();
            await _cache.RemoveAsync(AllKey(userId)); // invalidate stale list

            if (category.ParentCategoryId.HasValue && category.ParentCategory == null)
            {
                category.ParentCategory = await _context.TransactionCategories.FindAsync(category.ParentCategoryId.Value);
            }

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
            IsTransferCategory = c.IsTransferCategory,
            ParentCategoryId = c.ParentCategoryId,
            ParentCategoryName = c.ParentCategory?.Name
        };
    }

    private string ToTitleCase(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return input;
        return System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(input.ToLower());
    }
}