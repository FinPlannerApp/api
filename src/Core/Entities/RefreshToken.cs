using Microsoft.EntityFrameworkCore;

namespace Domain.Entities;

[Owned] // This tells EF Core it's an "owned" entity type, part of ApplicationUser
public class RefreshToken
{
    public int Id { get; set; }
    public required string Token { get; set; }
    public DateTime ExpiresUtc { get; set; }
    public DateTime CreatedUtc { get; set; }
    public required string CreatedByIp { get; set; }
    public DateTime? RevokedUtc { get; set; }
    public string? RevokedByIp { get; set; }

    // A "session" is one sign-in on one device. Every token rotated from the same login shares
    // the SessionId, which is also the "SessionId" claim in the access token.
    public string? SessionId { get; set; }
    public DateTime? SessionStartedUtc { get; set; }
    // Random id from the server-issued device cookie. The token only works with that cookie.
    public string? DeviceId { get; set; }
    public string? UserAgent { get; set; }

    public bool IsExpired => DateTime.UtcNow >= ExpiresUtc;
    public bool IsActive => RevokedUtc == null && !IsExpired;
}
