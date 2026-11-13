namespace Application;

/// <summary>Bound from the "Security:Sessions" configuration section.</summary>
public class SessionSecuritySettings
{
    public const string SectionName = "Security:Sessions";

    /// <summary>Most signed-in devices per user. Signing in beyond this signs out the least recently active one.</summary>
    public int MaxSessionsPerUser { get; set; } = 10;

    /// <summary>Email the user when a login comes from a device they have not used before.</summary>
    public bool NotifyOnNewDevice { get; set; } = true;

    /// <summary>
    /// Require an emailed code before a login from a new device completes. Off by default: it needs
    /// Redis and a working mail queue, so switch it on once you have seen the new-device emails work.
    /// </summary>
    public bool RequireCodeOnNewDevice { get; set; } = false;

    /// <summary>
    /// A just-rotated refresh token that is presented again within this many seconds is treated as
    /// a race between two tabs, not theft. After that, reuse signs the whole session out.
    /// </summary>
    public int RefreshReuseGraceSeconds { get; set; } = 30;
}
