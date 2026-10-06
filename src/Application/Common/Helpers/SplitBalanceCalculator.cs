using Domain.Entities.Split;

namespace Application.Common.Helpers;

public class MemberBalance
{
    public int MemberId { get; set; }
    public string MemberName { get; set; } = string.Empty;

    /// <summary>Money this member actually paid out for expenses (settlements NOT included).</summary>
    public decimal TotalPaid { get; set; }
    /// <summary>This member's fair share of all expenses.</summary>
    public decimal TotalShare { get; set; }
    /// <summary>Confirmed settlement payments this member has made.</summary>
    public decimal SettledPaid { get; set; }
    /// <summary>Confirmed settlement payments this member has received.</summary>
    public decimal SettledReceived { get; set; }
    /// <summary>Sent-but-unconfirmed settlements: + for sender, - for receiver. Not part of NetBalance; only feeds the plan.</summary>
    public decimal InTransit { get; set; }
    /// <summary>TotalPaid - TotalShare + SettledPaid - SettledReceived. Positive = owed money.</summary>
    public decimal NetBalance { get; set; }
}

public class SimplifiedDebt
{
    public int FromMemberId { get; set; }
    public string FromMemberName { get; set; } = string.Empty;
    public int ToMemberId { get; set; }
    public string ToMemberName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
}

public static class SplitBalanceCalculator
{
    private const int MaxMembersForOptimalSearch = 18;

    public static List<MemberBalance> CalculateNetBalances(SplitGroup group)
    {
        var balances = group.Members.ToDictionary(
            m => m.Id,
            m => new MemberBalance { MemberId = m.Id, MemberName = m.Name });

        foreach (var expense in group.Expenses)
        {
            foreach (var payer in expense.Payers)
            {
                if (balances.TryGetValue(payer.SplitGroupMemberId, out var b))
                    b.TotalPaid += payer.AmountPaid;
            }
            foreach (var participant in expense.Participants)
            {
                if (balances.TryGetValue(participant.SplitGroupMemberId, out var b))
                    b.TotalShare += participant.ShareAmount;
            }
        }

        foreach (var settlement in group.Settlements)
        {
            if (settlement.Status == SettlementStatus.Completed)
            {
                if (balances.TryGetValue(settlement.FromMemberId, out var from)) from.SettledPaid += settlement.Amount;
                if (balances.TryGetValue(settlement.ToMemberId, out var to)) to.SettledReceived += settlement.Amount;
            }
            else if (settlement.Status == SettlementStatus.AwaitingConfirmation)
            {
                if (balances.TryGetValue(settlement.FromMemberId, out var from)) from.InTransit += settlement.Amount;
                if (balances.TryGetValue(settlement.ToMemberId, out var to)) to.InTransit -= settlement.Amount;
            }
        }

        foreach (var b in balances.Values)
            b.NetBalance = b.TotalPaid - b.TotalShare + b.SettledPaid - b.SettledReceived;

        return balances.Values.OrderBy(b => b.MemberId).ToList();
    }

    /// <summary>
    /// Smallest possible list of payments. Minimum = (people with a balance) - (max number of disjoint
    /// zero-sum sub-groups); found exactly by bitmask DP up to 18 people, greedy beyond that.
    /// Built from NetBalance + InTransit so a payment already sent isn't requested again.
    /// </summary>
    public static List<SimplifiedDebt> SimplifyDebts(List<MemberBalance> balances)
    {
        var people = balances
            .Select(b => (b.MemberId, b.MemberName, Paise: ToPaise(b.NetBalance + b.InTransit)))
            .Where(p => p.Paise != 0)
            .OrderBy(p => p.MemberId)
            .ToList();

        if (people.Count < 2) return new List<SimplifiedDebt>();

        // Push any 1-2 paise rounding residue onto the largest balance so everything sums to zero.
        var residue = people.Sum(p => p.Paise);
        if (residue != 0)
        {
            var idx = 0;
            for (var i = 1; i < people.Count; i++)
                if (Math.Abs(people[i].Paise) > Math.Abs(people[idx].Paise)) idx = i;
            people[idx] = (people[idx].MemberId, people[idx].MemberName, people[idx].Paise - residue);
            if (people[idx].Paise == 0) people.RemoveAt(idx);
        }

        var groups = people.Count <= MaxMembersForOptimalSearch
            ? PartitionIntoZeroSumGroups(people.Select(p => p.Paise).ToArray())
            : new List<List<int>> { Enumerable.Range(0, people.Count).ToList() };

        var result = new List<SimplifiedDebt>();
        foreach (var component in groups)
        {
            var members = component.Select(i => people[i]).ToList();
            result.AddRange(SettleGreedy(members));
        }

        return result
            .OrderByDescending(d => d.Amount)
            .ThenBy(d => d.FromMemberId)
            .ThenBy(d => d.ToMemberId)
            .ToList();
    }

    private static List<List<int>> PartitionIntoZeroSumGroups(long[] values)
    {
        var n = values.Length;
        var size = 1 << n;
        var sums = new long[size];
        var dp = new sbyte[size];
        var prev = new sbyte[size];

        for (var mask = 1; mask < size; mask++)
        {
            var low = System.Numerics.BitOperations.TrailingZeroCount(mask);
            sums[mask] = sums[mask & (mask - 1)] + values[low];
        }

        for (var mask = 1; mask < size; mask++)
        {
            var best = -1;
            var bestPrev = -1;
            for (var i = 0; i < n; i++)
            {
                if ((mask & (1 << i)) == 0) continue;
                var candidate = dp[mask ^ (1 << i)];
                if (candidate > best)
                {
                    best = candidate;
                    bestPrev = i;
                }
            }
            dp[mask] = (sbyte)(best + (sums[mask] == 0 ? 1 : 0));
            prev[mask] = (sbyte)bestPrev;
        }

        var order = new List<int>();
        var cur = size - 1;
        while (cur != 0)
        {
            var i = prev[cur];
            order.Add(i);
            cur ^= 1 << i;
        }
        order.Reverse();

        var groups = new List<List<int>>();
        var currentGroup = new List<int>();
        long running = 0;
        foreach (var i in order)
        {
            currentGroup.Add(i);
            running += values[i];
            if (running == 0)
            {
                groups.Add(currentGroup);
                currentGroup = new List<int>();
            }
        }
        if (currentGroup.Count > 0) groups.Add(currentGroup);

        return groups;
    }

    private static List<SimplifiedDebt> SettleGreedy(List<(int MemberId, string MemberName, long Paise)> members)
    {
        var debtors = members.Where(m => m.Paise < 0).Select(m => (m.MemberId, m.MemberName, Left: -m.Paise)).ToList();
        var creditors = members.Where(m => m.Paise > 0).Select(m => (m.MemberId, m.MemberName, Left: m.Paise)).ToList();
        var result = new List<SimplifiedDebt>();

        while (debtors.Count > 0 && creditors.Count > 0)
        {
            int di = -1, ci = -1;

            for (var d = 0; d < debtors.Count && di < 0; d++)
                for (var c = 0; c < creditors.Count; c++)
                    if (debtors[d].Left == creditors[c].Left) { di = d; ci = c; break; }

            if (di < 0)
            {
                di = 0; ci = 0;
                for (var d = 1; d < debtors.Count; d++) if (debtors[d].Left > debtors[di].Left) di = d;
                for (var c = 1; c < creditors.Count; c++) if (creditors[c].Left > creditors[ci].Left) ci = c;
            }

            var pay = Math.Min(debtors[di].Left, creditors[ci].Left);
            result.Add(new SimplifiedDebt
            {
                FromMemberId = debtors[di].MemberId,
                FromMemberName = debtors[di].MemberName,
                ToMemberId = creditors[ci].MemberId,
                ToMemberName = creditors[ci].MemberName,
                Amount = pay / 100m
            });

            debtors[di] = (debtors[di].MemberId, debtors[di].MemberName, debtors[di].Left - pay);
            creditors[ci] = (creditors[ci].MemberId, creditors[ci].MemberName, creditors[ci].Left - pay);
            if (debtors[di].Left == 0) debtors.RemoveAt(di);
            if (creditors[ci].Left == 0) creditors.RemoveAt(ci);
        }

        return result;
    }

    public static List<(string Category, decimal Amount, int Count)> CategoryTotals(IEnumerable<SplitExpense> expenses)
        => expenses
            .GroupBy(e => string.IsNullOrWhiteSpace(e.Category) ? "General" : e.Category!)
            .Select(g => (Category: g.Key, Amount: g.Sum(e => e.Amount), Count: g.Count()))
            .OrderByDescending(x => x.Amount)
            .ToList();

    private static long ToPaise(decimal amount) => (long)Math.Round(amount * 100m, MidpointRounding.AwayFromZero);
}