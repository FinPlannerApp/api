using System;
using System.Collections.Generic;
using System.Linq;
using Application.Common.Helpers;
using Domain.Entities.Split;
using FluentAssertions;
using Xunit;

namespace Application.Tests.Common.Helpers;

/// <summary>
/// SplitBalanceCalculator is pure logic operating on in-memory domain
/// objects — no database required. Verified directly against the real
/// implementation in api-main/src/Application/Common/Helpers/
/// SplitBalanceCalculator.cs, including the debt-simplification
/// algorithm (SimplifyDebts) discovered during this verification pass.
/// </summary>
public class SplitBalanceCalculatorTests
{
    [Fact]
    public void CalculateNetBalances_SimpleTwoPersonExpense_SplitsCorrectly()
    {
        // Alice pays 1000 for an expense shared equally between Alice
        // and Bob (500 each) — Alice should be owed 500, Bob should owe 500.
        var alice = new SplitGroupMember { Id = 1, Name = "Alice" };
        var bob = new SplitGroupMember { Id = 2, Name = "Bob" };

        var expense = new SplitExpense
        {
            Description = "Dinner",
            Amount = 1000,
            SplitType = SplitType.Equal,
            Payers = new List<SplitExpensePayer>
            {
                new() { SplitGroupMemberId = 1, AmountPaid = 1000 }
            },
            Participants = new List<SplitExpenseParticipant>
            {
                new() { SplitGroupMemberId = 1, ShareAmount = 500 },
                new() { SplitGroupMemberId = 2, ShareAmount = 500 }
            }
        };

        var group = new SplitGroup
        {
            Name = "Test Group",
            CreatedByUserId = "user-1",
            ShareToken = "token",
            Members = new List<SplitGroupMember> { alice, bob },
            Expenses = new List<SplitExpense> { expense },
            Settlements = new List<SplitSettlement>()
        };

        var balances = SplitBalanceCalculator.CalculateNetBalances(group);

        balances.Single(b => b.MemberId == 1).NetBalance.Should().Be(500m);
        balances.Single(b => b.MemberId == 2).NetBalance.Should().Be(-500m);
    }

    [Fact]
    public void CalculateNetBalances_CompletedSettlement_MovesBothMembersTowardZero()
    {
        // Bob owes Alice 500 from an expense. A completed settlement of
        // 500 from Bob to Alice should bring both balances to zero —
        // Bob has now "paid off" his debt, Alice has "received" what
        // she was owed.
        var alice = new SplitGroupMember { Id = 1, Name = "Alice" };
        var bob = new SplitGroupMember { Id = 2, Name = "Bob" };

        var expense = new SplitExpense
        {
            Description = "Dinner",
            Amount = 1000,
            SplitType = SplitType.Equal,
            Payers = new List<SplitExpensePayer> { new() { SplitGroupMemberId = 1, AmountPaid = 1000 } },
            Participants = new List<SplitExpenseParticipant>
            {
                new() { SplitGroupMemberId = 1, ShareAmount = 500 },
                new() { SplitGroupMemberId = 2, ShareAmount = 500 }
            }
        };

        var settlement = new SplitSettlement
        {
            FromMemberId = 2,
            ToMemberId = 1,
            Amount = 500,
            PaymentReference = "ref-1",
            Status = SettlementStatus.Completed
        };

        var group = new SplitGroup
        {
            Name = "Test Group",
            CreatedByUserId = "user-1",
            ShareToken = "token",
            Members = new List<SplitGroupMember> { alice, bob },
            Expenses = new List<SplitExpense> { expense },
            Settlements = new List<SplitSettlement> { settlement }
        };

        var balances = SplitBalanceCalculator.CalculateNetBalances(group);

        balances.Single(b => b.MemberId == 1).NetBalance.Should().Be(0m);
        balances.Single(b => b.MemberId == 2).NetBalance.Should().Be(0m);
    }

    [Fact]
    public void CalculateNetBalances_PendingSettlement_DoesNotAffectBalance()
    {
        // The same scenario as above, but the settlement is still
        // Pending — it must NOT move any balance, since it hasn't
        // actually been confirmed as received.
        var alice = new SplitGroupMember { Id = 1, Name = "Alice" };
        var bob = new SplitGroupMember { Id = 2, Name = "Bob" };

        var expense = new SplitExpense
        {
            Description = "Dinner",
            Amount = 1000,
            SplitType = SplitType.Equal,
            Payers = new List<SplitExpensePayer> { new() { SplitGroupMemberId = 1, AmountPaid = 1000 } },
            Participants = new List<SplitExpenseParticipant>
            {
                new() { SplitGroupMemberId = 1, ShareAmount = 500 },
                new() { SplitGroupMemberId = 2, ShareAmount = 500 }
            }
        };

        var pendingSettlement = new SplitSettlement
        {
            FromMemberId = 2,
            ToMemberId = 1,
            Amount = 500,
            PaymentReference = "ref-1",
            Status = SettlementStatus.Pending
        };

        var group = new SplitGroup
        {
            Name = "Test Group",
            CreatedByUserId = "user-1",
            ShareToken = "token",
            Members = new List<SplitGroupMember> { alice, bob },
            Expenses = new List<SplitExpense> { expense },
            Settlements = new List<SplitSettlement> { pendingSettlement }
        };

        var balances = SplitBalanceCalculator.CalculateNetBalances(group);

        balances.Single(b => b.MemberId == 1).NetBalance.Should().Be(500m);
        balances.Single(b => b.MemberId == 2).NetBalance.Should().Be(-500m);
    }

    [Fact]
    public void SimplifyDebts_ThreeMemberChain_ProducesMinimumNumberOfPayments()
    {
        // A owes B 300, B owes C 300 — the naive approach is two
        // payments (A→B, B→C). The simplification should collapse this
        // to a single payment: A→C for 300, since B's position nets to
        // zero and doesn't need to touch any money at all.
        var balances = new List<MemberBalance>
        {
            new() { MemberId = 1, MemberName = "A", NetBalance = -300m },
            new() { MemberId = 2, MemberName = "B", NetBalance = 0m },
            new() { MemberId = 3, MemberName = "C", NetBalance = 300m }
        };

        var result = SplitBalanceCalculator.SimplifyDebts(balances);

        result.Should().HaveCount(1);
        result[0].FromMemberId.Should().Be(1);
        result[0].ToMemberId.Should().Be(3);
        result[0].Amount.Should().Be(300m);
    }

    [Fact]
    public void SimplifyDebts_AllBalancesZero_ProducesNoPayments()
    {
        var balances = new List<MemberBalance>
        {
            new() { MemberId = 1, MemberName = "A", NetBalance = 0m },
            new() { MemberId = 2, MemberName = "B", NetBalance = 0m }
        };

        var result = SplitBalanceCalculator.SimplifyDebts(balances);

        result.Should().BeEmpty();
    }

    [Fact]
    public void SimplifyDebts_OneDebtorTwoCreditors_SplitsPaymentAcrossBoth()
    {
        // A owes 300 total, split between two creditors B (owed 100)
        // and C (owed 200) — A's single debt should resolve into two
        // separate payments, one to each creditor, for exactly their
        // owed amount.
        var balances = new List<MemberBalance>
        {
            new() { MemberId = 1, MemberName = "A", NetBalance = -300m },
            new() { MemberId = 2, MemberName = "B", NetBalance = 100m },
            new() { MemberId = 3, MemberName = "C", NetBalance = 200m }
        };

        var result = SplitBalanceCalculator.SimplifyDebts(balances);

        result.Should().HaveCount(2);
        result.Sum(d => d.Amount).Should().Be(300m);
        result.Should().OnlyContain(d => d.FromMemberId == 1);
    }
}
