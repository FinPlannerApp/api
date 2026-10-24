using Application.DTOs.Split;
using Domain.Entities.Split;

namespace Application.Common.Helpers;

/// <summary>
/// Pure aggregation behind the cross-group dashboard: for one user, what they owe or are owed
/// per person, summed over every group (and one-to-one friend ledger) they are in.
/// </summary>
public static class SplitDashboardBuilder
{
    /// <summary>A single payment that would settle one line: debtor -> creditor inside one group.</summary>
    public record PersonDebt(int GroupId, string Currency, int FromMemberId, int ToMemberId, decimal Amount);

    public static SplitDashboardDto Build(string userId, IEnumerable<SplitGroup> groups)
    {
        var people = new Dictionary<string, DashboardPersonDto>();
        var groupCount = 0;

        foreach (var (group, me) in ActiveGroups(userId, groups))
        {
            groupCount++;
            foreach (var other in group.Members.Where(m => m.Id != me.Id && m.LeftAt == null))
            {
                var key = KeyOf(other);
                if (!people.TryGetValue(key, out var person))
                    people[key] = person = new DashboardPersonDto { Key = key, Name = other.Name };
                if (group.IsDirect) person.DirectGroupId = group.Id;
            }

            foreach (var line in LinesFor(group, me))
            {
                var person = people[KeyOf(line.Other)];
                person.Lines.Add(new DashboardLineDto
                {
                    GroupId = group.Id,
                    GroupName = group.IsDirect ? "Direct" : group.Name,
                    IsDirect = group.IsDirect,
                    Currency = group.Currency,
                    Amount = line.Amount
                });
            }
        }

        foreach (var person in people.Values)
        {
            person.Totals = person.Lines
                .GroupBy(l => l.Currency)
                .Select(g => new DashboardTotalDto { Currency = g.Key, Net = Math.Round(g.Sum(l => l.Amount), 2) })
                .Where(t => t.Net != 0)
                .OrderBy(t => t.Currency).ToList();
            person.Lines = person.Lines.OrderBy(l => l.Currency).ThenByDescending(l => Math.Abs(l.Amount)).ToList();
        }

        var totals = people.Values.SelectMany(p => p.Lines)
            .GroupBy(l => l.Currency)
            .Select(g => new DashboardCurrencyTotalDto
            {
                Currency = g.Key,
                YouAreOwed = Math.Round(g.Where(l => l.Amount > 0).Sum(l => l.Amount), 2),
                YouOwe = Math.Round(-g.Where(l => l.Amount < 0).Sum(l => l.Amount), 2)
            }).OrderBy(t => t.Currency).ToList();

        return new SplitDashboardDto
        {
            Totals = totals,
            GroupCount = groupCount,
            People = people.Values
                .OrderByDescending(p => p.Totals.Sum(t => Math.Abs(t.Net)))
                .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList()
        };
    }

    /// <summary>The payments that would settle everything with one person in one currency.</summary>
    public static List<PersonDebt> DebtsWith(string userId, IEnumerable<SplitGroup> groups, string personKey, string currency)
    {
        var result = new List<PersonDebt>();
        foreach (var (group, me) in ActiveGroups(userId, groups))
        {
            if (!string.Equals(group.Currency, currency, StringComparison.OrdinalIgnoreCase)) continue;
            foreach (var line in LinesFor(group, me).Where(l => KeyOf(l.Other) == personKey))
            {
                result.Add(line.Amount > 0
                    ? new PersonDebt(group.Id, group.Currency, line.Other.Id, me.Id, line.Amount)
                    : new PersonDebt(group.Id, group.Currency, me.Id, line.Other.Id, -line.Amount));
            }
        }
        return result;
    }

    public static string KeyOf(SplitGroupMember m)
        => m.LinkedUserId != null ? "u:" + m.LinkedUserId : "m:" + m.Id;

    private static IEnumerable<(SplitGroup Group, SplitGroupMember Me)> ActiveGroups(string userId, IEnumerable<SplitGroup> groups)
    {
        foreach (var g in groups)
        {
            if (g.Status == SplitGroupStatus.Archived) continue;
            var me = g.Members.FirstOrDefault(m => m.LinkedUserId == userId && m.LeftAt == null);
            if (me != null) yield return (g, me);
        }
    }

    /// <summary>What the group's payment plan says I owe / am owed, per other person. Positive = they owe me.</summary>
    private static IEnumerable<(SplitGroupMember Other, decimal Amount)> LinesFor(SplitGroup group, SplitGroupMember me)
    {
        var plan = SplitBalanceCalculator.BuildPlan(group, SplitBalanceCalculator.CalculateNetBalances(group));
        var byId = group.Members.ToDictionary(m => m.Id);
        foreach (var debt in plan)
        {
            if (debt.ToMemberId == me.Id && byId.TryGetValue(debt.FromMemberId, out var debtor))
                yield return (debtor, debt.Amount);
            else if (debt.FromMemberId == me.Id && byId.TryGetValue(debt.ToMemberId, out var creditor))
                yield return (creditor, -debt.Amount);
        }
    }
}
