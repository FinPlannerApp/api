using Application.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Application.Common.Helpers;

public static class CategoryRollup
{
    /// <summary>
    /// Expands a category ID into itself plus every direct subcategory.
    /// For a top-level category with children, returns [parent, child1,
    /// child2, ...]. For a subcategory or a childless category, returns
    /// just [categoryId] — so calling this is always safe, even for
    /// categories that don't participate in any hierarchy.
    ///
    /// Only ONE level deep, matching the validation rule enforced in
    /// TransactionCategoryService — no recursion needed, and deliberately
    /// no recursion supported, so this can't silently do something
    /// surprising if bad data ever got in.
    /// </summary>
    public static async Task<List<int>> ExpandCategoryIdsAsync(
        IApplicationDbContext context,
        int categoryId,
        CancellationToken cancellationToken = default)
    {
        var ids = new List<int> { categoryId };

        var childIds = await context.TransactionCategories
            .AsNoTracking()
            .Where(c => c.ParentCategoryId == categoryId)
            .Select(c => c.Id)
            .ToListAsync(cancellationToken);

        ids.AddRange(childIds);
        return ids;
    }

    /// <summary>
    /// Bulk version — builds a lookup of every parent category to its own
    /// ID plus its children's IDs, in a single query. Use this when
    /// processing MANY budgets at once (GetBudgetProgressQuery,
    /// GetFinancialHealthQuery) rather than calling the single version in
    /// a loop, which would issue one query per budget.
    /// </summary>
    public static async Task<Dictionary<int, List<int>>> BuildRollupMapAsync(
        IApplicationDbContext context,
        string userId,
        CancellationToken cancellationToken = default)
    {
        var allCategories = await context.TransactionCategories
            .AsNoTracking()
            .Where(c => c.UserId == userId)
            .Select(c => new { c.Id, c.ParentCategoryId })
            .ToListAsync(cancellationToken);

        var map = new Dictionary<int, List<int>>();

        foreach (var category in allCategories)
        {
            // Every category maps to at least itself.
            if (!map.ContainsKey(category.Id))
                map[category.Id] = new List<int> { category.Id };

            // And every child also gets added to its parent's list.
            if (category.ParentCategoryId.HasValue)
            {
                var parentId = category.ParentCategoryId.Value;
                if (!map.ContainsKey(parentId))
                    map[parentId] = new List<int> { parentId };

                map[parentId].Add(category.Id);
            }
        }

        return map;
    }

    /// <summary>
    /// Convenience for the common "does this transaction's category count
    /// toward this budget's category" check. Handles the null budget
    /// category (an "all categories" budget) case too — that always
    /// matches, same as the existing behavior everywhere.
    /// </summary>
    public static bool Matches(
        Dictionary<int, List<int>> rollupMap,
        int? budgetCategoryId,
        int? transactionCategoryId)
    {
        if (budgetCategoryId is null) return true;      // "all categories" budget
        if (transactionCategoryId is null) return false; // uncategorized transaction

        return rollupMap.TryGetValue(budgetCategoryId.Value, out var ids)
            && ids.Contains(transactionCategoryId.Value);
    }
}
