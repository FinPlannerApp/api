using Application.Common.Models;
using Application.DTOs.Auth;

namespace Application.Contracts;

public interface IAuthService
{
    /// <param name="deviceId">The id from the server-issued device cookie (the controller always supplies one).</param>
    Task<Result<LoginResponseDto>> LoginAsync(LoginUserDto dto, string ipAddress, string userAgent, string deviceId);
    Task<Result<string>> InitiateRegistrationAsync(RegisterUserDto dto);
    Task<Result<string>> CompleteRegistrationAsync(VerifyRegistrationDto dto);
    Task<Result<LoginResponseDto>> RefreshTokenAsync(string refreshToken, string ipAddress, string deviceId);
    Task<Result<bool>> LogoutAsync(string refreshToken, string ipAddress);
    Task<Result<string>> ForgotPasswordAsync(string email);
    Task<Result<bool>> VerifyOtpAsync(VerifyOtpDto dto);
    Task<Result<bool>> ResetPasswordAsync(ResetPasswordDto dto);
    Task<Result<bool>> CheckEmailAsync(string email);
    Task<Result<bool>> CheckUserNameAsync(string username);
    Task<Result<UserProfileDto>> UpdateProfileAsync(string userId, UpdateProfileDto dto);

    Task<Result<List<SessionDto>>> GetSessionsAsync(string userId, string? currentSessionId);
    Task<Result<bool>> RevokeSessionAsync(string userId, string sessionId, string ipAddress);
    /// <summary>Signs out every device except the current one.</summary>
    Task<Result<bool>> RevokeOtherSessionsAsync(string userId, string? currentSessionId, string ipAddress);
}
