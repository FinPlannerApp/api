using System;
using System.Collections.Generic;
using Application.Common.Helpers;
using FluentAssertions;
using Xunit;

namespace Application.Tests.Common.Helpers;

/// <summary>
/// CategoryRollup.Matches is a pure, parameter-only function — the
/// rollup map is constructed directly here rather than fetched via
/// BuildRollupMapAsync, since Matches itself has no database
/// dependency. Verified directly against the real implementation in
/// api-main/src/Application/Common/Helpers/CategoryRollup.cs.
/// </summary>
public class CategoryRollupTests
{
    [Fact]
    public void Matches_TransactionOnParentCategory_MatchesBudgetOnSameParent()
    {
        var rollupMap = new Dictionary<int, List<int>> { { 10, new List<int> { 10, 11, 12 } } };

        var result = CategoryRollup.Matches(rollupMap, budgetCategoryId: 10, transactionCategoryId: 10);

        result.Should().BeTrue();
    }

    [Fact]
    public void Matches_TransactionOnSubcategory_MatchesBudgetOnParentCategory()
    {
        // Category 10 is the parent budgeted category; 11 and 12 are its
        // subcategories. A transaction logged directly against
        // subcategory 11 must still count toward the parent's budget.
        var rollupMap = new Dictionary<int, List<int>> { { 10, new List<int> { 10, 11, 12 } } };

        var result = CategoryRollup.Matches(rollupMap, budgetCategoryId: 10, transactionCategoryId: 11);

        result.Should().BeTrue();
    }

    [Fact]
    public void Matches_TransactionOnUnrelatedCategory_DoesNotMatch()
    {
        var rollupMap = new Dictionary<int, List<int>> { { 10, new List<int> { 10, 11, 12 } } };

        var result = CategoryRollup.Matches(rollupMap, budgetCategoryId: 10, transactionCategoryId: 99);

        result.Should().BeFalse();
    }

    [Fact]
    public void Matches_BudgetHasNoCategory_AlwaysMatchesRegardlessOfTransaction()
    {
        // A null budgetCategoryId represents an "all categories" budget
        // — it must match every transaction, including one with no
        // category of its own.
        var rollupMap = new Dictionary<int, List<int>>();

        CategoryRollup.Matches(rollupMap, budgetCategoryId: null, transactionCategoryId: 42).Should().BeTrue();
        CategoryRollup.Matches(rollupMap, budgetCategoryId: null, transactionCategoryId: null).Should().BeTrue();
    }

    [Fact]
    public void Matches_TransactionHasNoCategory_NeverMatchesASpecificBudget()
    {
        // An uncategorized transaction cannot satisfy a budget that IS
        // scoped to a specific category — this is the inverse of the
        // "all categories" case above.
        var rollupMap = new Dictionary<int, List<int>> { { 10, new List<int> { 10 } } };

        var result = CategoryRollup.Matches(rollupMap, budgetCategoryId: 10, transactionCategoryId: null);

        result.Should().BeFalse();
    }

    [Fact]
    public void Matches_BudgetCategoryNotInRollupMap_ReturnsFalseRatherThanThrowing()
    {
        // A budget category that (for whatever reason) isn't present in
        // the rollup map at all — the lookup should fail gracefully to
        // false, not throw a KeyNotFoundException.
        var rollupMap = new Dictionary<int, List<int>> { { 10, new List<int> { 10 } } };

        var act = () => CategoryRollup.Matches(rollupMap, budgetCategoryId: 999, transactionCategoryId: 10);

        act.Should().NotThrow();
        act().Should().BeFalse();
    }
}
