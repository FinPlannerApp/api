using System.ComponentModel.DataAnnotations;

namespace Application.DTOs.Auth;

public class LoginUserDto
{
    [Required]
    public required string UserName { get; set; }

    [Required]
    public required string Password { get; set; }

    /// <summary>Obsolete: several devices can now be signed in at once, so there is nothing to force. Ignored.</summary>
    public bool ForceLogin { get; set; } = false;

    /// <summary>The emailed code, sent on the second step when a login from a new device needs verifying.</summary>
    public string? DeviceVerificationCode { get; set; }
}
