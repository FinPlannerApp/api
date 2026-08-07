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

    // All computed server-side, not stored — always reflects the current
    // real state rather than a snapshot that could drift out of date.
    public decimal CurrentAmount { get; set; }
    public decimal ProgressPercent { get; set; }
    public decimal? RequiredMonthlySaving { get; set; } // null if no TargetDate set — there's nothing to pace against
}

public class UpsertGoalDto
{
    public int? Id { get; set; }
    public required string Name { get; set; }
    public decimal TargetAmount { get; set; }
    public DateTime? TargetDate { get; set; }
    public int? SavingsBucketId { get; set; }

    // Only meaningful/used when SavingsBucketId is null
    public decimal ManualCurrentAmount { get; set; } = 0;
}
