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
    [Fact]
    public void CalculateNetBalances_KeepsPaidShareAndSettledSeparate_SoTheyAddUpToNet()
    {
        var group = TwoPersonDinnerGroup(new SplitSettlement
        {
            FromMemberId = 2,
            ToMemberId = 1,
            Amount = 200,
            PaymentReference = "r",
            Status = SettlementStatus.Completed
        });

        var balances = SplitBalanceCalculator.CalculateNetBalances(group);
        var alice = balances.Single(b => b.MemberId == 1);
        var bob = balances.Single(b => b.MemberId == 2);

        alice.TotalPaid.Should().Be(1000m);
        alice.TotalShare.Should().Be(500m);
        alice.SettledReceived.Should().Be(200m);
        alice.NetBalance.Should().Be(300m);

        bob.TotalPaid.Should().Be(0m);
        bob.TotalShare.Should().Be(500m);
        bob.SettledPaid.Should().Be(200m);
        bob.NetBalance.Should().Be(-300m);

        foreach (var b in balances)
            b.NetBalance.Should().Be(b.TotalPaid - b.TotalShare + b.SettledPaid - b.SettledReceived);
    }

    [Fact]
    public void CalculateNetBalances_AwaitingConfirmation_IsTrackedAsInTransitNotNet()
    {
        var group = TwoPersonDinnerGroup(new SplitSettlement
        {
            FromMemberId = 2,
            ToMemberId = 1,
            Amount = 500,
            PaymentReference = "r",
            Status = SettlementStatus.AwaitingConfirmation
        });

        var balances = SplitBalanceCalculator.CalculateNetBalances(group);

        balances.Single(b => b.MemberId == 1).NetBalance.Should().Be(500m);
        balances.Single(b => b.MemberId == 1).InTransit.Should().Be(-500m);
        balances.Single(b => b.MemberId == 2).NetBalance.Should().Be(-500m);
        balances.Single(b => b.MemberId == 2).InTransit.Should().Be(500m);
    }

    [Fact]
    public void SimplifyDebts_PaymentInTransit_IsNotRequestedAgain()
    {
        var group = TwoPersonDinnerGroup(new SplitSettlement
        {
            FromMemberId = 2,
            ToMemberId = 1,
            Amount = 500,
            PaymentReference = "r",
            Status = SettlementStatus.AwaitingConfirmation
        });

        var plan = SplitBalanceCalculator.SimplifyDebts(SplitBalanceCalculator.CalculateNetBalances(group));

        plan.Should().BeEmpty();
    }

    [Fact]
    public void SimplifyDebts_FindsDisjointSubgroups_ThatSettleAmongThemselves()
    {
        var balances = new List<MemberBalance>
        {
            new() { MemberId = 1, MemberName = "A", NetBalance = -100m },
            new() { MemberId = 2, MemberName = "B", NetBalance = -200m },
            new() { MemberId = 3, MemberName = "C", NetBalance = 100m },
            new() { MemberId = 4, MemberName = "D", NetBalance = 200m }
        };

        var plan = SplitBalanceCalculator.SimplifyDebts(balances);

        plan.Should().HaveCount(2);
        plan.Should().Contain(d => d.FromMemberId == 1 && d.ToMemberId == 3 && d.Amount == 100m);
        plan.Should().Contain(d => d.FromMemberId == 2 && d.ToMemberId == 4 && d.Amount == 200m);
    }

    [Fact]
    public void SimplifyDebts_FiveMembers_UsesThreePaymentsAndClearsEveryone()
    {
        var balances = new List<MemberBalance>
        {
            new() { MemberId = 1, MemberName = "A", NetBalance = 250m },
            new() { MemberId = 2, MemberName = "B", NetBalance = 200m },
            new() { MemberId = 3, MemberName = "C", NetBalance = -250m },
            new() { MemberId = 4, MemberName = "D", NetBalance = 250m },
            new() { MemberId = 5, MemberName = "E", NetBalance = -450m }
        };

        var plan = SplitBalanceCalculator.SimplifyDebts(balances);

        plan.Should().HaveCount(3);
        AssertPlanClearsAll(balances, plan);
    }

    [Fact]
    public void SimplifyDebts_EveryPlanClearsEveryBalance_ForManyRandomGroups()
    {
        var rng = new Random(12345);
        for (var round = 0; round < 300; round++)
        {
            var n = rng.Next(2, 10);
            var net = new List<decimal>();
            for (var i = 0; i < n - 1; i++) net.Add(rng.Next(-5000, 5001) / 100m);
            net.Add(-net.Sum());

            var balances = net.Select((v, i) => new MemberBalance { MemberId = i + 1, MemberName = "M" + i, NetBalance = v }).ToList();
            var plan = SplitBalanceCalculator.SimplifyDebts(balances);

            AssertPlanClearsAll(balances, plan);
            plan.Should().OnlyContain(d => d.Amount > 0m);
            plan.Count.Should().BeLessThanOrEqualTo(Math.Max(0, balances.Count(b => b.NetBalance != 0m) - 1));
        }
    }

    [Fact]
    public void SimplifyDebts_ToleratesAPaisaOfRoundingResidue()
    {
        var balances = new List<MemberBalance>
        {
            new() { MemberId = 1, MemberName = "A", NetBalance = -50.00m },
            new() { MemberId = 2, MemberName = "B", NetBalance = 50.01m }
        };

        var plan = SplitBalanceCalculator.SimplifyDebts(balances);

        plan.Should().HaveCount(1);
        plan[0].Amount.Should().Be(50.00m);
    }

    private static void AssertPlanClearsAll(List<MemberBalance> balances, List<SimplifiedDebt> plan)
    {
        var left = balances.ToDictionary(b => b.MemberId, b => b.NetBalance);
        foreach (var d in plan)
        {
            left[d.FromMemberId] += d.Amount;
            left[d.ToMemberId] -= d.Amount;
        }
        left.Values.Should().OnlyContain(v => Math.Abs(v) <= 0.01m);
    }

    private static SplitGroup TwoPersonDinnerGroup(SplitSettlement settlement)
    {
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

        return new SplitGroup
        {
            Name = "Test Group",
            CreatedByUserId = "user-1",
            ShareToken = "token",
            Members = new List<SplitGroupMember>
            {
                new() { Id = 1, Name = "Alice" },
                new() { Id = 2, Name = "Bob" }
            },
            Expenses = new List<SplitExpense> { expense },
            Settlements = new List<SplitSettlement> { settlement }
        };
    }

    // ── Pairwise ("simplify debts" off) ─────────────────────────────────────────

    /// <summary>A paid 100 for B alone; B paid 100 for C alone. Net: A +100, B 0, C -100.</summary>
    private static SplitGroup ChainGroup(bool simplify, params SplitSettlement[] settlements)
    {
        SplitExpense Single(int payer, int consumer) => new()
        {
            Description = "x",
            Amount = 100,
            SplitType = SplitType.Equal,
            Payers = new List<SplitExpensePayer> { new() { SplitGroupMemberId = payer, AmountPaid = 100 } },
            Participants = new List<SplitExpenseParticipant> { new() { SplitGroupMemberId = consumer, ShareAmount = 100 } }
        };

        return new SplitGroup
        {
            Name = "Chain",
            CreatedByUserId = "user-1",
            ShareToken = "token",
            SimplifyDebts = simplify,
            Members = new List<SplitGroupMember>
            {
                new() { Id = 1, Name = "A" }, new() { Id = 2, Name = "B" }, new() { Id = 3, Name = "C" }
            },
            Expenses = new List<SplitExpense> { Single(1, 2), Single(2, 3) },
            Settlements = settlements.ToList()
        };
    }

    [Fact]
    public void PairwiseDebts_DoesNotRerouteThroughThirdParties()
    {
        var group = ChainGroup(simplify: false);

        var plan = SplitBalanceCalculator.BuildPlan(group, SplitBalanceCalculator.CalculateNetBalances(group));

        plan.Should().HaveCount(2);
        plan.Should().ContainSingle(d => d.FromMemberId == 2 && d.ToMemberId == 1 && d.Amount == 100m);
        plan.Should().ContainSingle(d => d.FromMemberId == 3 && d.ToMemberId == 2 && d.Amount == 100m);
    }

    [Fact]
    public void SimplifiedPlan_CollapsesTheSameChainIntoOnePayment()
    {
        var group = ChainGroup(simplify: true);

        var plan = SplitBalanceCalculator.BuildPlan(group, SplitBalanceCalculator.CalculateNetBalances(group));

        plan.Should().ContainSingle();
        plan[0].FromMemberId.Should().Be(3);
        plan[0].ToMemberId.Should().Be(1);
        plan[0].Amount.Should().Be(100m);
    }

    [Fact]
    public void PairwiseDebts_CompletedPaymentReducesOnlyThatPair()
    {
        var group = ChainGroup(false, new SplitSettlement
        {
            FromMemberId = 2, ToMemberId = 1, Amount = 100, PaymentReference = "r", Status = SettlementStatus.Completed
        });

        var plan = SplitBalanceCalculator.PairwiseDebts(group);

        plan.Should().ContainSingle();
        plan[0].FromMemberId.Should().Be(3);
        plan[0].ToMemberId.Should().Be(2);
    }

    [Fact]
    public void PairwiseDebts_OppositeDebtsBetweenTwoPeopleAreNetted()
    {
        var group = ChainGroup(false);
        // B also consumed 40 that A paid for -> B owes A 140; and A consumed 30 B paid for -> net B owes A 110.
        group.Expenses.Add(new SplitExpense
        {
            Description = "y", Amount = 40, SplitType = SplitType.Equal,
            Payers = new List<SplitExpensePayer> { new() { SplitGroupMemberId = 1, AmountPaid = 40 } },
            Participants = new List<SplitExpenseParticipant> { new() { SplitGroupMemberId = 2, ShareAmount = 40 } }
        });
        group.Expenses.Add(new SplitExpense
        {
            Description = "z", Amount = 30, SplitType = SplitType.Equal,
            Payers = new List<SplitExpensePayer> { new() { SplitGroupMemberId = 2, AmountPaid = 30 } },
            Participants = new List<SplitExpenseParticipant> { new() { SplitGroupMemberId = 1, ShareAmount = 30 } }
        });

        var plan = SplitBalanceCalculator.PairwiseDebts(group);

        plan.Should().ContainSingle(d => d.FromMemberId == 2 && d.ToMemberId == 1 && d.Amount == 110m);
    }

    [Fact]
    public void IsFullySettled_PairwiseGroupNeedsEveryPairSquare_NotJustZeroNets()
    {
        // C pays A directly: every net balance is now zero, but B->A and C->B are still open pairwise.
        var payment = new SplitSettlement
        {
            FromMemberId = 3, ToMemberId = 1, Amount = 100, PaymentReference = "r", Status = SettlementStatus.Completed
        };

        var pairwise = ChainGroup(false, payment);
        var simplified = ChainGroup(true, payment);

        SplitBalanceCalculator.IsFullySettled(simplified, SplitBalanceCalculator.CalculateNetBalances(simplified))
            .Should().BeTrue();
        SplitBalanceCalculator.IsFullySettled(pairwise, SplitBalanceCalculator.CalculateNetBalances(pairwise))
            .Should().BeFalse();
    }

    [Fact]
    public void PairwiseDebts_MultiplePayersShareTheCreditProportionally()
    {
        // 300 dinner; A paid 200, B paid 100; C's share is 100 -> C owes A 66.67 and B 33.33.
        var group = new SplitGroup
        {
            Name = "g", CreatedByUserId = "u", ShareToken = "t", SimplifyDebts = false,
            Members = new List<SplitGroupMember>
            {
                new() { Id = 1, Name = "A" }, new() { Id = 2, Name = "B" }, new() { Id = 3, Name = "C" }
            },
            Expenses = new List<SplitExpense>
            {
                new()
                {
                    Description = "Dinner", Amount = 300, SplitType = SplitType.Equal,
                    Payers = new List<SplitExpensePayer>
                    {
                        new() { SplitGroupMemberId = 1, AmountPaid = 200 },
                        new() { SplitGroupMemberId = 2, AmountPaid = 100 }
                    },
                    Participants = new List<SplitExpenseParticipant>
                    {
                        new() { SplitGroupMemberId = 1, ShareAmount = 100 },
                        new() { SplitGroupMemberId = 2, ShareAmount = 100 },
                        new() { SplitGroupMemberId = 3, ShareAmount = 100 }
                    }
                }
            },
            Settlements = new List<SplitSettlement>()
        };

        var plan = SplitBalanceCalculator.PairwiseDebts(group);

        plan.Should().Contain(d => d.FromMemberId == 3 && d.ToMemberId == 1 && d.Amount == 66.67m);
        plan.Should().Contain(d => d.FromMemberId == 3 && d.ToMemberId == 2 && d.Amount == 33.33m);
        // B owes A: B's share of A's payment (200/300 * 100 = 66.67) minus A's share of B's payment (100/300 * 100 = 33.33).
        plan.Should().Contain(d => d.FromMemberId == 2 && d.ToMemberId == 1 && d.Amount == 33.33m);
    }
}

