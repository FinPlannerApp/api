using Application.Common.Models;
using Application.DTOs.Auth;

namespace Application.Contracts;

public interface IAuthService
{
    Task<Result<LoginResponseDto>> LoginAsync(LoginUserDto dto, string ipAddress, string userAgent);
    Task<Result<string>> InitiateRegistrationAsync(RegisterUserDto dto);
    Task<Result<string>> CompleteRegistrationAsync(VerifyRegistrationDto dto);
    Task<Result<LoginResponseDto>> RefreshTokenAsync(string refreshToken, string ipAddress);
    Task<Result<bool>> LogoutAsync(string refreshToken, string ipAddress);
    Task<Result<string>> ForgotPasswordAsync(string email);
    Task<Result<bool>> VerifyOtpAsync(VerifyOtpDto dto);
    Task<Result<bool>> ResetPasswordAsync(ResetPasswordDto dto);
    Task<Result<bool>> CheckEmailAsync(string email);
    Task<Result<bool>> CheckUserNameAsync(string username);
    Task<Result<UserProfileDto>> UpdateProfileAsync(string userId, UpdateProfileDto dto);
}