using Domain.Entities.Split;

namespace Application.Common.Helpers;

public class MemberBalance
{
    public int MemberId { get; set; }
    public string MemberName { get; set; } = string.Empty;

    public decimal TotalPaid { get; set; }
    public decimal TotalShare { get; set; }

    /// <summary>Positive = this member is owed money overall. Negative = this member owes money overall.</summary>
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
    /// <summary>
    /// Net balance per member = everything they've paid across every
    /// expense, minus everything they owe as a participant, adjusted by
    /// completed settlements (a completed settlement moves the paying
    /// member's balance up and the receiving member's balance down —
    /// they've effectively "paid off" that amount of what they owed, or
    /// "received" that amount of what they were owed).
    /// </summary>
    public static List<MemberBalance> CalculateNetBalances(SplitGroup group)
    {
        var balances = group.Members.ToDictionary(
            m => m.Id,
            m => new MemberBalance { MemberId = m.Id, MemberName = m.Name, TotalPaid = 0, TotalShare = 0, NetBalance = 0 });

        foreach (var expense in group.Expenses)
        {
            foreach (var payer in expense.Payers)
            {
                if (balances.TryGetValue(payer.SplitGroupMemberId, out var b))
                {
                    b.TotalPaid += payer.AmountPaid;
                    b.NetBalance += payer.AmountPaid;
                }
            }
            foreach (var participant in expense.Participants)
            {
                if (balances.TryGetValue(participant.SplitGroupMemberId, out var b))
                {
                    b.TotalShare += participant.ShareAmount;
                    b.NetBalance -= participant.ShareAmount;
                }
            }
        }

        foreach (var settlement in group.Settlements.Where(s => s.Status == SettlementStatus.Completed))
        {
            if (balances.TryGetValue(settlement.FromMemberId, out var from))
            {
                from.TotalPaid += settlement.Amount;
                from.NetBalance += settlement.Amount;
            }
            if (balances.TryGetValue(settlement.ToMemberId, out var to))
            {
                to.NetBalance -= settlement.Amount;
            }
        }

        return balances.Values.ToList();
    }

    /// <summary>
    /// The standard greedy debt-simplification algorithm — repeatedly
    /// match the largest debtor against the largest creditor until every
    /// balance reaches zero. Minimizes the number of actual payments
    /// needed to settle the whole group, rather than everyone paying
    /// everyone else individually.
    /// </summary>
    public static List<SimplifiedDebt> SimplifyDebts(List<MemberBalance> balances)
    {
        var debtors = balances.Where(b => b.NetBalance < -0.01m)
            .OrderByDescending(b => -b.NetBalance)
            .Select(b => (b.MemberId, b.MemberName, Amount: -b.NetBalance))
            .ToList();

        var creditors = balances.Where(b => b.NetBalance > 0.01m)
            .OrderByDescending(b => b.NetBalance)
            .Select(b => (b.MemberId, b.MemberName, Amount: b.NetBalance))
            .ToList();

        var result = new List<SimplifiedDebt>();
        var debtorAmounts = debtors.Select(d => d.Amount).ToList();
        var creditorAmounts = creditors.Select(c => c.Amount).ToList();

        int i = 0, j = 0;
        while (i < debtors.Count && j < creditors.Count)
        {
            var settleAmount = Math.Min(debtorAmounts[i], creditorAmounts[j]);

            if (settleAmount > 0.01m)
            {
                result.Add(new SimplifiedDebt
                {
                    FromMemberId = debtors[i].MemberId,
                    FromMemberName = debtors[i].MemberName,
                    ToMemberId = creditors[j].MemberId,
                    ToMemberName = creditors[j].MemberName,
                    Amount = Math.Round(settleAmount, 2)
                });
            }

            debtorAmounts[i] -= settleAmount;
            creditorAmounts[j] -= settleAmount;

            if (debtorAmounts[i] <= 0.01m) i++;
            if (creditorAmounts[j] <= 0.01m) j++;
        }

        return result;
    }
}
