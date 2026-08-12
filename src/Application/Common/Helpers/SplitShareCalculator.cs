namespace Application.Common.Helpers;

public class ExpenseParticipantInput
{
    public int MemberId { get; set; }
    public decimal? ExactAmount { get; set; }   // used when SplitType == Exact
    public decimal? Percentage { get; set; }    // used when SplitType == Percentage
    public decimal? Shares { get; set; }        // used when SplitType == Shares
}

public static class SplitShareCalculator
{
    public static List<(int MemberId, decimal Share)> ComputeShares(
        Domain.Entities.Split.SplitType type, decimal totalAmount, List<ExpenseParticipantInput> participants)
    {
        if (participants.Count == 0)
            throw new InvalidOperationException("An expense needs at least one participant.");

        switch (type)
        {
            case Domain.Entities.Split.SplitType.Equal:
            {
                var equalShare = Math.Round(totalAmount / participants.Count, 2);
                var shares = participants.Select(p => (p.MemberId, Share: equalShare)).ToList();

                // Equal division can leave a rounding remainder (e.g.
                // ₹100 / 3 = ₹33.33 each, leaving ₹0.01 unaccounted for).
                // The last participant absorbs it, so the sum always
                // exactly equals the real total.
                var remainder = totalAmount - shares.Sum(s => s.Share);
                if (remainder != 0)
                {
                    var last = shares[^1];
                    shares[^1] = (last.MemberId, last.Share + remainder);
                }
                return shares;
            }

            case Domain.Entities.Split.SplitType.Exact:
            {
                var shares = participants.Select(p => (p.MemberId, Share: p.ExactAmount ?? 0)).ToList();
                var sum = shares.Sum(s => s.Share);
                if (Math.Abs(sum - totalAmount) > 0.01m)
                    throw new InvalidOperationException(
                        $"Exact amounts sum to {sum:F2}, but the expense total is {totalAmount:F2} — they need to match exactly.");
                return shares;
            }

            case Domain.Entities.Split.SplitType.Percentage:
            {
                var totalPercent = participants.Sum(p => p.Percentage ?? 0);
                if (Math.Abs(totalPercent - 100) > 0.01m)
                    throw new InvalidOperationException($"Percentages sum to {totalPercent}%, but must sum to 100%.");

                var percentageShares = participants
                    .Select(p => (p.MemberId, Share: Math.Round(totalAmount * (p.Percentage ?? 0) / 100m, 2)))
                    .ToList();

                var percentageRemainder = totalAmount - percentageShares.Sum(s => s.Share);
                if (percentageRemainder != 0 && percentageShares.Count > 0)
                {
                    var last = percentageShares[^1];
                    percentageShares[^1] = (last.MemberId, last.Share + percentageRemainder);
                }
                return percentageShares;
            }

            case Domain.Entities.Split.SplitType.Shares:
            {
                var totalShares = participants.Sum(p => p.Shares ?? 0);
                if (totalShares <= 0)
                    throw new InvalidOperationException("Total shares must be greater than zero.");

                var shareShares = participants
                    .Select(p => (p.MemberId, Share: Math.Round(totalAmount * (p.Shares ?? 0) / totalShares, 2)))
                    .ToList();

                var sharesRemainder = totalAmount - shareShares.Sum(s => s.Share);
                if (sharesRemainder != 0 && shareShares.Count > 0)
                {
                    var last = shareShares[^1];
                    shareShares[^1] = (last.MemberId, last.Share + sharesRemainder);
                }
                return shareShares;
            }

            default:
                throw new ArgumentOutOfRangeException(nameof(type));
        }
    }
}
