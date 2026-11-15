namespace Application.DTOs.Auth;

public class UpdateProfileDto
{
    public required string Name { get; set; }

    // Null/omitted = not changing email. Only populate CurrentPassword
    // when actually changing the email — name-only updates don't need it.
    public string? NewEmail { get; set; }
    public string? CurrentPassword { get; set; }
    // 6-digit code emailed to NewEmail. First call without it sends the code; second call with it applies the change.
    public string? EmailCode { get; set; }
    /// <summary>Null = leave the current setting alone.</summary>
    public bool? OverspendAlertsEnabled { get; set; }
}

public class UserProfileDto
{
    public required string Name { get; set; }
    public required string Email { get; set; }
    public bool OverspendAlertsEnabled { get; set; }
}
