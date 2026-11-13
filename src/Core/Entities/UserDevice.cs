using Microsoft.EntityFrameworkCore;

namespace Domain.Entities;

/// <summary>A browser/app the user has signed in from, identified by the server-issued device cookie.</summary>
[Owned]
public class UserDevice
{
    public int Id { get; set; }
    public required string DeviceId { get; set; }
    public DateTime FirstSeenUtc { get; set; }
    public DateTime LastSeenUtc { get; set; }
    public string? LastIp { get; set; }
    public string? UserAgent { get; set; }
}
