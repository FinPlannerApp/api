namespace Application.DTOs.Auth;

public class UpdateProfileDto
{
    public required string Name { get; set; }

    // Null/omitted = not changing email. Only populate CurrentPassword
    // when actually changing the email — name-only updates don't need it.
    public string? NewEmail { get; set; }
    public string? CurrentPassword { get; set; }
    public bool OverspendAlertsEnabled { get; set; } = true;
}

public class UserProfileDto
{
    public required string Name { get; set; }
    public required string Email { get; set; }
    public bool OverspendAlertsEnabled { get; set; }
}
