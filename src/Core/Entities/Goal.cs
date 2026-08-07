namespace Domain.Entities;

/// <summary>
/// Two ways to track progress, deliberately: link to a SavingsBucket
/// (built earlier this session) for automatic tracking derived from real
/// allocated money, or track manually for people who don't use buckets.
/// A goal isn't required to have a bucket — flexibility over forcing one
/// workflow.
/// </summary>
public class Goal : BaseEntity
{
    public required string UserId { get; set; }
    public required string Name { get; set; }
    public decimal TargetAmount { get; set; }
    public DateTime? TargetDate { get; set; }
    public bool IsAchieved { get; set; } = false;

    // If set, progress is DERIVED from the bucket's AllocatedAmount —
    // ManualCurrentAmount is ignored in that case. If null,
    // ManualCurrentAmount is the source of truth instead.
    public int? SavingsBucketId { get; set; }
    public SavingsBucket? SavingsBucket { get; set; }

    public decimal ManualCurrentAmount { get; set; } = 0;
}
