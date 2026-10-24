using System.Collections.Generic;
using System.Linq;
using Application.Common.Helpers;
using Domain.Entities.Split;
using FluentAssertions;
using Xunit;

namespace Application.Tests.Common.Helpers;

public class SplitDashboardBuilderTests
{
    private const string Me = "me";
    private const string Asha = "asha";

    private static SplitExpense Paid(int payer, int consumer, decimal amount) => new()
    {
        Description = "x", Amount = amount, SplitType = SplitType.Equal,
        Payers = new List<SplitExpensePayer> { new() { SplitGroupMemberId = payer, AmountPaid = amount } },
        Participants = new List<SplitExpenseParticipant> { new() { SplitGroupMemberId = consumer, ShareAmount = amount } }
    };

    /// <summary>Two linked people (me = first id, Asha = second id) with the given expenses.</summary>
    private static SplitGroup Pair(int id, int meId, int ashaId, string currency, bool direct, params SplitExpense[] expenses) => new()
    {
        Id = id, Name = "G" + id, CreatedByUserId = Me, ShareToken = "t" + id, Currency = currency, IsDirect = direct,
        Members = new List<SplitGroupMember>
        {
            new() { Id = meId, Name = "Me", LinkedUserId = Me },
            new() { Id = ashaId, Name = "Asha", LinkedUserId = Asha }
        },
        Expenses = expenses.ToList(),
        Settlements = new List<SplitSettlement>()
    };

    [Fact]
    public void Build_MergesTheSamePersonAcrossGroups_AndNetsThem()
    {
        // Goa: Asha owes me 300. Direct: I owe Asha 120. Net: Asha owes me 180.
        var goa = Pair(1, 1, 2, "INR", false, Paid(1, 2, 300));
        var direct = Pair(2, 3, 4, "INR", true, Paid(4, 3, 120));

        var dash = SplitDashboardBuilder.Build(Me, new[] { goa, direct });

        dash.People.Should().ContainSingle();
        var asha = dash.People[0];
        asha.Key.Should().Be("u:" + Asha);
        asha.DirectGroupId.Should().Be(2);
        asha.Totals.Should().ContainSingle(t => t.Currency == "INR" && t.Net == 180m);
        asha.Lines.Should().HaveCount(2);
        dash.Totals.Should().ContainSingle(t => t.YouAreOwed == 300m && t.YouOwe == 120m);
    }

    [Fact]
    public void Build_KeepsCurrenciesSeparate()
    {
        var inr = Pair(1, 1, 2, "INR", false, Paid(1, 2, 100));
        var usd = Pair(2, 3, 4, "USD", false, Paid(4, 3, 50));

        var dash = SplitDashboardBuilder.Build(Me, new[] { inr, usd });

        dash.People[0].Totals.Should().Contain(t => t.Currency == "INR" && t.Net == 100m);
        dash.People[0].Totals.Should().Contain(t => t.Currency == "USD" && t.Net == -50m);
        dash.Totals.Should().HaveCount(2);
    }

    [Fact]
    public void Build_ListsAFriendWithNothingOwedAndSkipsGroupsIAmNotIn()
    {
        var empty = Pair(1, 1, 2, "INR", true);
        var notMine = Pair(2, 3, 4, "INR", false, Paid(3, 4, 99));
        notMine.Members.First().LinkedUserId = "someone-else";

        var dash = SplitDashboardBuilder.Build(Me, new[] { empty, notMine });

        dash.People.Should().ContainSingle();
        dash.People[0].Totals.Should().BeEmpty();
        dash.GroupCount.Should().Be(1);
    }

    [Fact]
    public void Build_IgnoresArchivedGroups()
    {
        var closed = Pair(1, 1, 2, "INR", false, Paid(1, 2, 300));
        closed.Status = SplitGroupStatus.Archived;

        SplitDashboardBuilder.Build(Me, new[] { closed }).People.Should().BeEmpty();
    }

    [Fact]
    public void DebtsWith_PointsEachPaymentInTheDirectionOfTheDebt_AndFiltersByCurrency()
    {
        var owedToMe = Pair(1, 1, 2, "INR", false, Paid(1, 2, 300));
        var iOwe = Pair(2, 3, 4, "INR", true, Paid(4, 3, 120));
        var usd = Pair(3, 5, 6, "USD", false, Paid(5, 6, 10));

        var debts = SplitDashboardBuilder.DebtsWith(Me, new[] { owedToMe, iOwe, usd }, "u:" + Asha, "INR");

        debts.Should().HaveCount(2);
        debts.Should().ContainSingle(d => d.GroupId == 1 && d.FromMemberId == 2 && d.ToMemberId == 1 && d.Amount == 300m);
        debts.Should().ContainSingle(d => d.GroupId == 2 && d.FromMemberId == 3 && d.ToMemberId == 4 && d.Amount == 120m);
    }

    [Fact]
    public void Build_NameOnlyMembersStaySeparatePerGroup()
    {
        var a = Pair(1, 1, 2, "INR", false, Paid(1, 2, 50));
        a.Members.Last().LinkedUserId = null;
        var b = Pair(2, 3, 4, "INR", false, Paid(3, 4, 70));
        b.Members.Last().LinkedUserId = null;

        var dash = SplitDashboardBuilder.Build(Me, new[] { a, b });

        dash.People.Should().HaveCount(2);
        dash.People.Select(p => p.Key).Should().BeEquivalentTo("m:2", "m:4");
    }
}
