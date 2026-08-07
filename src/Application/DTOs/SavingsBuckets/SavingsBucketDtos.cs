namespace Application.DTOs.SavingsBuckets;

public class SavingsBucketDto
{
    public int Id { get; set; }
    public int AccountId { get; set; }
    public string AccountName { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal AllocatedAmount { get; set; }
    public decimal? TargetAmount { get; set; }
}

public class UpsertSavingsBucketDto
{
    public int? Id { get; set; }
    public int AccountId { get; set; }
    public required string Name { get; set; }
    public decimal AllocatedAmount { get; set; }
    public decimal? TargetAmount { get; set; }
}

/// <summary>
/// The account's balance broken down into what's allocated to buckets
/// vs. genuinely unallocated — what a "bucket view" of one account
/// actually needs to show.
/// </summary>
public class AccountBucketBreakdownDto
{
    public int AccountId { get; set; }
    public decimal AccountBalance { get; set; }
    public decimal TotalAllocated { get; set; }
    public decimal Unallocated { get; set; }
    public List<SavingsBucketDto> Buckets { get; set; } = new();
}
