namespace Application.DTOs.Auth;

/// <summary>One signed-in device, as shown in Settings &gt; Devices.</summary>
public class SessionDto
{
    public required string SessionId { get; set; }
    /// <summary>"Chrome on Windows" etc.</summary>
    public required string Device { get; set; }
    public string? IpAddress { get; set; }
    public DateTime SignedInUtc { get; set; }
    public DateTime LastActiveUtc { get; set; }
    public bool IsCurrent { get; set; }
}
