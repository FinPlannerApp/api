using Application.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.BackgroundJobs;

public class RewardPointsExpiryJob
{
    private readonly IApplicationDbContext _context;

    public RewardPointsExpiryJob(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task ExpirePointsAsync()
    {
        var now = DateTime.UtcNow;

        var expiring = await _context.CreditCardRewardPoints
            .Where(r => r.ExpiryDate.HasValue && r.ExpiryDate.Value <= now)
            .ToListAsync();

        foreach (var r in expiring)
        {
            var remaining = r.PointsEarned - r.PointsRedeemed - r.PointsExpired;
            if (remaining > 0)
            {
                // Only expire what's actually still remaining — if some
                // of this batch was already redeemed before its expiry
                // date, that portion was already accounted for.
                r.PointsExpired += remaining;
            }
        }

        if (expiring.Count > 0)
        {
            await _context.SaveChangesAsync(default);
        }
    }
}

