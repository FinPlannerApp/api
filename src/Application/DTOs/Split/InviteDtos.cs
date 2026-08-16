namespace Application.DTOs.Split;

public class CreateInviteDto
{
    public int GroupId { get; set; }
    public DateTime? ExpiresAt { get; set; } // null = never expires
    public int? MaxUses { get; set; }        // null = unlimited uses
}

public class InviteCreatedDto
{
    /// <summary>
    /// The plain token, shown exactly once. It's never retrievable again
    /// after this response — only its hash is stored.
    /// </summary>
    public string Token { get; set; } = string.Empty;
    public DateTime? ExpiresAt { get; set; }
    public int? MaxUses { get; set; }
}

/// <summary>
/// What an unauthenticated visitor sees before deciding to log in and
/// join — deliberately minimal. No balances, no expenses, no member UPI
/// IDs. Just enough to know what they're being invited into.
/// </summary>
public class InvitePreviewDto
{
    public string GroupName { get; set; } = string.Empty;
    public int MemberCount { get; set; }
    public bool IsValid { get; set; }
    public string? InvalidReason { get; set; } // "expired", "revoked", "fully used", "not found"
}

public class JoinGroupDto
{
    public required string Token { get; set; }
    public required string DisplayName { get; set; } // matches how CreateGroupDto.CreatorName already works — passed explicitly, not auto-derived from an unverified identity dependency
}

public class JoinGroupResultDto
{
    public int GroupId { get; set; }
    public int MemberId { get; set; }
    public bool AlreadyWasMember { get; set; } // true if this was a no-op — joining twice via the same link is safe, not an error
}
