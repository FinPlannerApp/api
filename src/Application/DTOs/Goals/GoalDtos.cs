namespace Application.DTOs.Goals;

public class GoalDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal TargetAmount { get; set; }
    public DateTime? TargetDate { get; set; }
    public bool IsAchieved { get; set; }
    public int? SavingsBucketId { get; set; }
    public string? SavingsBucketName { get; set; }
    public int? LinkedAccountId { get; set; }
    public string? LinkedAccountName { get; set; }
    /// <summary>Where CurrentAmount comes from: bucket | account | manual.</summary>
    public string Source { get; set; } = "manual";

    // All computed server-side, not stored — always reflects the current
    // real state rather than a snapshot that could drift out of date.
    public decimal CurrentAmount { get; set; }
    public decimal ProgressPercent { get; set; }
    public decimal? RequiredMonthlySaving { get; set; } // null if no TargetDate set — there's nothing to pace against

    /// <summary>achieved | no-date | overdue | on-track | behind | not-started</summary>
    public string Status { get; set; } = "no-date";
    /// <summary>Where straight-line progress from creation to the target date would be by today.</summary>
    public decimal? ExpectedPercent { get; set; }
    /// <summary>When the goal completes if the pace so far continues (null until there is progress to extrapolate).</summary>
    public DateTime? ProjectedDate { get; set; }
    /// <summary>Next of 25 / 50 / 75 / 100 still ahead, null once achieved.</summary>
    public int? NextMilestonePercent { get; set; }
    public int MilestonesReached { get; set; }
}

public class UpsertGoalDto
{
    public int? Id { get; set; }
    public required string Name { get; set; }
    public decimal TargetAmount { get; set; }
    public DateTime? TargetDate { get; set; }
    public int? SavingsBucketId { get; set; }
    /// <summary>Follow this account's live balance (used when no bucket is chosen).</summary>
    public int? LinkedAccountId { get; set; }

    // Only meaningful/used when SavingsBucketId is null
    public decimal ManualCurrentAmount { get; set; } = 0;
}
