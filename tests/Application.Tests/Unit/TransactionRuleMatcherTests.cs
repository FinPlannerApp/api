using System.Collections.Generic;
using Application.Common.Helpers;
using Domain.Entities;
using Domain.Enums;
using FluentAssertions;
using Xunit;

namespace Application.Tests.Unit;

public class TransactionRuleMatcherTests
{
    private static TransactionRule Rule(string text, int mode = 0, int priority = 100, int id = 1) =>
        new() { UserId = "u", Id = id, MatchText = text, MatchMode = mode, Priority = priority, TransactionCategoryId = id };

    [Theory]
    [InlineData("Swiggy order 123", 0, true)]
    [InlineData("SWIGGY", 2, true)]
    [InlineData("Order Swiggy", 1, false)]
    [InlineData("Swiggy Instamart", 1, true)]
    [InlineData("Zomato", 0, false)]
    public void Matches_HonoursModeAndIgnoresCase(string description, int mode, bool expected)
        => TransactionRuleMatcher.Matches(Rule("swiggy", mode), description, 100m, TransactionType.Expense).Should().Be(expected);

    [Fact]
    public void Matches_RespectsTypeAndAmountRange()
    {
        var r = Rule("bus"); r.TypeFilter = TransactionType.Expense; r.MinAmount = 10; r.MaxAmount = 50;
        TransactionRuleMatcher.Matches(r, "Bus", 16m, TransactionType.Expense).Should().BeTrue();
        TransactionRuleMatcher.Matches(r, "Bus", 5m, TransactionType.Expense).Should().BeFalse();
        TransactionRuleMatcher.Matches(r, "Bus", 60m, TransactionType.Expense).Should().BeFalse();
        TransactionRuleMatcher.Matches(r, "Bus", 16m, TransactionType.Income).Should().BeFalse();
    }

    [Fact]
    public void Matches_IgnoresInactiveAndBlankRules()
    {
        var off = Rule("bus"); off.IsActive = false;
        TransactionRuleMatcher.Matches(off, "Bus", 1m, TransactionType.Expense).Should().BeFalse();
        TransactionRuleMatcher.Matches(Rule("  "), "Bus", 1m, TransactionType.Expense).Should().BeFalse();
    }

    [Fact]
    public void FirstMatch_PicksLowestPriorityThenOldest()
    {
        var rules = new List<TransactionRule> { Rule("bus", priority: 50, id: 3), Rule("bus", priority: 10, id: 2), Rule("bus", priority: 10, id: 1) };
        TransactionRuleMatcher.FirstMatch(rules, "City bus", 10m, TransactionType.Expense)!.Id.Should().Be(1);
        TransactionRuleMatcher.FirstMatch(rules, "Taxi", 10m, TransactionType.Expense).Should().BeNull();
    }
}
