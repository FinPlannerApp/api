using Application.Contracts;
using Application.DTOs.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace API.Controllers;

[Route("api/[controller]")]
public class AuthController : BaseController
{
    private readonly IAuthService _authService;

    public AuthController(IAuthService authService)
    {
        _authService = authService;
    }

    private string GetIpAddress()
    {
        if (Request.Headers.ContainsKey("X-Forwarded-For"))
            return Request.Headers["X-Forwarded-For"].ToString();

        return HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }

    [HttpPost("register")]
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    public async Task<IActionResult> Register([FromBody] RegisterUserDto dto)
    {
        var result = await _authService.InitiateRegistrationAsync(dto);
        return HandleResult(result);
    }

    [HttpPost("verify-registration")]
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    public async Task<IActionResult> VerifyRegistration([FromBody] VerifyRegistrationDto dto)
    {
        var result = await _authService.CompleteRegistrationAsync(dto);
        return HandleResult(result);
    }

    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    public async Task<IActionResult> Login([FromBody] LoginUserDto dto)
    {
        var userAgent = Request.Headers["User-Agent"].ToString();
        var result = await _authService.LoginAsync(dto, GetIpAddress(), userAgent);
        return HandleResult(result);
    }

    [HttpPost("refresh")]
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    public async Task<IActionResult> Refresh([FromBody] RefreshTokenDto dto)
    {
        var result = await _authService.RefreshTokenAsync(dto.RefreshToken, GetIpAddress());
        return HandleResult(result);
    }

    [HttpPost("logout")]
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    public async Task<IActionResult> Logout([FromBody] RefreshTokenDto dto)
    {
        var result = await _authService.LogoutAsync(dto.RefreshToken, GetIpAddress());
        return HandleResult(result);
    }

    [HttpPost("forgot-password")]
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordDto dto)
    {
        var result = await _authService.ForgotPasswordAsync(dto.Email);
        return HandleResult(result);
    }

    [HttpPost("verify-otp")]
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    public async Task<IActionResult> VerifyOtp([FromBody] VerifyOtpDto dto)
    {
        var result = await _authService.VerifyOtpAsync(dto);
        return HandleResult(result);
    }

    [HttpPost("reset-password")]
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordDto dto)
    {
        var result = await _authService.ResetPasswordAsync(dto);
        return HandleResult(result);
    }

    [HttpPost("check-email")]
    [AllowAnonymous]
    public async Task<IActionResult> CheckEmail([FromBody] CheckEmailDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Email)) return BadRequest("Email is required.");
        var result = await _authService.CheckEmailAsync(dto.Email);
        return HandleResult(result);
    }

    [HttpPost("check-username")]
    [AllowAnonymous]
    public async Task<IActionResult> CheckUserName([FromBody] CheckUsernameDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Username)) return BadRequest("Username is required.");
        var result = await _authService.CheckUserNameAsync(dto.Username);
        return HandleResult(result);
    }

    [HttpPost("update-profile")]
    [Authorize]
    public async Task<IActionResult> UpdateProfile([FromBody] UpdateProfileDto dto)
    {
        var result = await _authService.UpdateProfileAsync(UserId!, dto);
        return HandleResult(result);
    }
}