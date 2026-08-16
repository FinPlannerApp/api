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

        // Same member listed twice would create two separate share
        // records for one person, double-counting them in every balance
        // calculation downstream — checked once here, before any
        // split-type-specific logic runs.
        var duplicateIds = participants
            .GroupBy(p => p.MemberId)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();
        if (duplicateIds.Any())
            throw new InvalidOperationException(
                $"Member ID(s) {string.Join(", ", duplicateIds)} appear more than once as a participant.");

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
                if (participants.Any(p => p.ExactAmount == null))
                    throw new InvalidOperationException("Every participant needs an exact amount for an Exact split — none can be left blank.");
                if (participants.Any(p => p.ExactAmount < 0))
                    throw new InvalidOperationException("Exact amounts can't be negative.");

                var shares = participants.Select(p => (p.MemberId, Share: p.ExactAmount!.Value)).ToList();
                var sum = shares.Sum(s => s.Share);
                if (Math.Abs(sum - totalAmount) > 0.01m)
                    throw new InvalidOperationException(
                        $"Exact amounts sum to {sum:F2}, but the expense total is {totalAmount:F2} — they need to match exactly.");
                return shares;
            }

            case Domain.Entities.Split.SplitType.Percentage:
            {
                if (participants.Any(p => p.Percentage == null))
                    throw new InvalidOperationException("Every participant needs a percentage for a Percentage split — none can be left blank.");
                if (participants.Any(p => p.Percentage < 0))
                    throw new InvalidOperationException("Percentages can't be negative.");

                var totalPercent = participants.Sum(p => p.Percentage ?? 0);
                if (Math.Abs(totalPercent - 100) > 0.01m)
                    throw new InvalidOperationException($"Percentages sum to {totalPercent}%, but must sum to 100%.");

                var percentageShares = participants
                    .Select(p => (p.MemberId, Share: Math.Round(totalAmount * p.Percentage!.Value / 100m, 2)))
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
                if (participants.Any(p => p.Shares == null))
                    throw new InvalidOperationException("Every participant needs a share count for a Shares split — none can be left blank.");
                // Zero or negative shares are rejected rather than
                // allowed as "participates but owes nothing" — someone
                // who genuinely owes nothing for an expense shouldn't be
                // listed as a participant on it at all. Treating this as
                // invalid input catches the more likely case: a value
                // that was meant to be entered but wasn't.
                if (participants.Any(p => p.Shares <= 0))
                    throw new InvalidOperationException("Shares must be greater than zero for every participant.");

                var totalShares = participants.Sum(p => p.Shares ?? 0);

                var shareShares = participants
                    .Select(p => (p.MemberId, Share: Math.Round(totalAmount * p.Shares!.Value / totalShares, 2)))
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
