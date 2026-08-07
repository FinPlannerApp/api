using Microsoft.AspNetCore.Identity;

namespace Domain.Entities;

public class ApplicationUser : IdentityUser, IAuditable
{
    public required string Name { get; set; }
    public DateTime DateOfBirth { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime LastSeenUtc { get; set; }
    
    // Session Tracking
    public string? CurrentSessionId { get; set; }
    public DateTime? LastLoginTime { get; set; }
    public string? LastKnownIp { get; set; }
    public string? LastKnownUserAgent { get; set; }

    /// <summary>
    /// The one real notification preference that currently exists —
    /// controls whether overspend alert emails send. Defaults true so
    /// existing users keep getting alerts unless they explicitly opt out.
    /// </summary>
    public bool OverspendAlertsEnabled { get; set; } = true;

    public List<RefreshToken> RefreshTokens { get; set; } = new();
}