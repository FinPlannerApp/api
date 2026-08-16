namespace Domain.Entities.Split;

public class SplitGroupInvite : BaseEntity
{
    public int SplitGroupId { get; set; }
    public SplitGroup SplitGroup { get; set; } = null!;

    public required string CreatedByUserId { get; set; }

    /// <summary>
    /// SHA-256 of the actual invite token. The plain token is shown to
    /// the creator once, at creation time, embedded in the invite
    /// link — never stored anywhere. A database compromise alone
    /// can't be used to join groups; the original token has to have
    /// actually been seen.
    /// </summary>
    public required string TokenHash { get; set; }

    public DateTime? ExpiresAt { get; set; }
    public int? MaxUses { get; set; }
    public int UsedCount { get; set; } = 0;
    public DateTime? RevokedAt { get; set; }
}
