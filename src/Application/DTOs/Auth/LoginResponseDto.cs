using System.ComponentModel.DataAnnotations;

namespace Application.DTOs.Auth;

public class LoginResponseDto
{
    public required string AccessToken { get; set; }
    public string? RefreshToken { get; set; }
    public required string UserName { get; set; }
}