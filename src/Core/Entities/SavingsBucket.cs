namespace Domain.Entities;

/// <summary>
/// A named, notional slice of one account's balance — "₹50k of my ₹200k
/// savings account is my Emergency Fund." Doesn't move any real money;
/// the underlying Account.Balance is unaffected by allocating to a
/// bucket. Purely a labeling/earmarking layer on top of what's already
/// there, which is exactly why the sum of all buckets for an account
/// must never exceed that account's actual balance — otherwise this
/// becomes actively misleading rather than useful.
/// </summary>
public class SavingsBucket : BaseEntity
{
    public required string UserId { get; set; }
    public int AccountId { get; set; }
    public Account Account { get; set; } = null!;
    public required string Name { get; set; }
    public decimal AllocatedAmount { get; set; }
    public decimal? TargetAmount { get; set; }
}
