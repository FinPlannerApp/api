using Application.Common.Models;
using Application.Contracts;
using Application.DTOs.Auth;
using Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using StackExchange.Redis;

namespace Application.Services;

public class AuthService : IAuthService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly JwtSettings _jwtSettings;
    private readonly ILogger<AuthService> _logger;
    private readonly IConnectionMultiplexer _redis;
    private readonly IEmailService _emailService;

    public AuthService(UserManager<ApplicationUser> userManager, IOptions<JwtSettings> jwtOptions, ILogger<AuthService> logger, IConnectionMultiplexer redis, IEmailService emailService)
    {
        _userManager = userManager;
        _jwtSettings = jwtOptions.Value;
        _logger = logger;
        _redis = redis;
        _emailService = emailService;
    }

    public async Task<Result<string>> InitiateRegistrationAsync(RegisterUserDto dto)
    {
        // Check if email already exists
        var existingUser = await _userManager.FindByEmailAsync(dto.Email);
        if (existingUser != null)
            return Result.Failure<string>(new Error("Auth.EmailTaken", "An account with this email already exists."));

        // Check if username already exists
        var existingUsername = await _userManager.FindByNameAsync(dto.UserName);
        if (existingUsername != null)
            return Result.Failure<string>(new Error("Auth.UsernameTaken", "This username is already taken."));

        // Generate OTP
        var otp = RandomNumberGenerator.GetInt32(100000, 999999).ToString();

        // Store pending registration data + OTP in Redis (10 min TTL)
        var db = _redis.GetDatabase();
        var registrationData = System.Text.Json.JsonSerializer.Serialize(dto);
        await db.StringSetAsync($"reg:{dto.Email}", registrationData, TimeSpan.FromMinutes(10));
        await db.StringSetAsync($"reg-otp:{dto.Email}", otp, TimeSpan.FromMinutes(10));

        // Send OTP email
        var htmlBody = $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8'>
    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
    <style>
        @import url('https://fonts.googleapis.com/css2?family=Outfit:wght@300;400;500;600;700&display=swap');
        body {{ margin: 0; padding: 0; font-family: 'Outfit', 'Segoe UI', Arial, sans-serif; background: linear-gradient(135deg, #0a0a0f, #111827, #0a0a0f); }}
        .wrapper {{ min-height: 100vh; padding: 40px 20px; background: linear-gradient(135deg, #0a0a0f 0%, #111827 50%, #0a0a0f 100%); }}
        .container {{ max-width: 520px; margin: 0 auto; }}
        .header {{ text-align: center; padding: 30px 0 20px; }}
        .header-icon {{ display: inline-block; width: 48px; height: 48px; background: linear-gradient(135deg, #059669, #0284c7); border-radius: 14px; line-height: 48px; font-size: 24px; margin-bottom: 12px; }}
        .header h1 {{ margin: 0; font-size: 22px; font-weight: 700; background: linear-gradient(135deg, #10b981, #38bdf8); -webkit-background-clip: text; -webkit-text-fill-color: transparent; background-clip: text; }}
        .card {{ background: rgba(255, 255, 255, 0.05); border: 1px solid rgba(255, 255, 255, 0.08); border-radius: 20px; padding: 36px 32px; }}
        .card p {{ color: #a1a1aa; font-size: 15px; line-height: 1.7; margin: 0 0 20px; }}
        .card p strong {{ color: #f4f4f5; }}
        .otp-container {{ text-align: center; margin: 28px 0; }}
        .otp-box {{ display: inline-block; background: linear-gradient(135deg, rgba(5, 150, 105, 0.15), rgba(2, 132, 199, 0.15)); border: 2px solid rgba(16, 185, 129, 0.3); border-radius: 16px; padding: 20px 40px; font-size: 38px; font-weight: 700; color: #10b981; letter-spacing: 8px; font-family: 'Outfit', monospace; }}
        .warning {{ background: rgba(251, 191, 36, 0.08); border: 1px solid rgba(251, 191, 36, 0.15); border-radius: 12px; padding: 14px 18px; margin-top: 24px; }}
        .warning p {{ color: #fbbf24; font-size: 13px; margin: 0; }}
        .footer {{ text-align: center; padding: 24px 0; }}
        .footer p {{ color: #52525b; font-size: 12px; margin: 0; }}
    </style>
</head>
<body>
    <div class='wrapper'>
        <div class='container'>
            <div class='header'>
                <div class='header-icon'>💰</div>
                <h1>Financial Planner</h1>
            </div>
            <div class='card'>
                <p style='font-size: 18px; color: #f4f4f5; font-weight: 600; margin-bottom: 8px;'>Verify Your Email</p>
                <p>Hello <strong>{dto.Name}</strong>, welcome to Financial Planner! Enter the code below to complete your registration.</p>
                <div class='otp-container'>
                    <div class='otp-box'>{otp}</div>
                </div>
                <p style='text-align: center; color: #71717a; font-size: 13px;'>This code is valid for <strong style='color: #10b981;'>10 minutes</strong></p>
                <div class='warning'>
                    <p>⚠️ If you did not create this account, please ignore this email.</p>
                </div>
            </div>
            <div class='footer'>
                <p>&copy; {DateTime.UtcNow.Year} Financial Planner</p>
            </div>
        </div>
    </div>
</body>
</html>";

        var mailRequest = new MailRequest
        {
            To = dto.Email,
            Subject = "Verify Your Email — Financial Planner",
            Body = htmlBody
        };

        await _emailService.SendEmailAsync(mailRequest);

        _logger.LogInformation("Registration OTP sent to {Email}", dto.Email);
        return Result.Success("Verification code sent to your email. Please enter the OTP to complete registration.");
    }

    public async Task<Result<string>> CompleteRegistrationAsync(VerifyRegistrationDto dto)
    {
        var db = _redis.GetDatabase();

        // Verify OTP
        var storedOtp = await db.StringGetAsync($"reg-otp:{dto.Email}");
        if (storedOtp.IsNullOrEmpty || storedOtp.ToString() != dto.Otp)
            return Result.Failure<string>(new Error("Auth.InvalidOtp", "Invalid or expired verification code."));

        // Retrieve cached registration data
        var registrationJson = await db.StringGetAsync($"reg:{dto.Email}");
        if (registrationJson.IsNullOrEmpty)
            return Result.Failure<string>(new Error("Auth.RegistrationExpired", "Registration session expired. Please register again."));

        var registrationDto = System.Text.Json.JsonSerializer.Deserialize<RegisterUserDto>(registrationJson.ToString());
        if (registrationDto == null)
            return Result.Failure<string>(new Error("Auth.RegistrationError", "Failed to process registration data."));

        // Create user
        var user = new ApplicationUser
        {
            Name = registrationDto.Name,
            DateOfBirth = DateTime.SpecifyKind(registrationDto.DateOfBirth, DateTimeKind.Utc),
            Email = registrationDto.Email,
            UserName = registrationDto.UserName,
            EmailConfirmed = true // Email is now verified via OTP
        };

        var result = await _userManager.CreateAsync(user, registrationDto.Password);

        if (!result.Succeeded)
        {
            var errors = result.Errors.Select(e => e.Description).ToList();
            _logger.LogWarning("User registration failed for {Email}. Errors: {Errors}", dto.Email, string.Join(", ", errors));
            return Result.Failure<string>(new Error("Auth.RegistrationFailed", $"Registration failed: {string.Join(", ", errors)}"));
        }

        // Clean up Redis keys
        await db.KeyDeleteAsync($"reg:{dto.Email}");
        await db.KeyDeleteAsync($"reg-otp:{dto.Email}");

        _logger.LogInformation("User registered successfully via OTP: {Email}", dto.Email);
        return Result.Success("Account created successfully! You can now log in.");
    }

    public async Task<Result<bool>> CheckEmailAsync(string email)
    {
        var user = await _userManager.FindByEmailAsync(email);
        return Result.Success(user == null); // true if available (null user), false if taken
    }

    public async Task<Result<bool>> CheckUserNameAsync(string username)
    {
        var user = await _userManager.FindByNameAsync(username);
        return Result.Success(user == null); // true if available (null user), false if taken
    }

    public async Task<Result<LoginResponseDto>> LoginAsync(LoginUserDto dto, string ipAddress, string userAgent)
    {
        var user = await _userManager.Users
            .Include(u => u.RefreshTokens) // Important: Load existing tokens
            .FirstOrDefaultAsync(u => u.UserName == dto.UserName);

        if (user == null || !await _userManager.CheckPasswordAsync(user, dto.Password))
        {
            _logger.LogWarning("Failed login attempt for {UserName} from IP {IpAddress}", dto.UserName, ipAddress);
            return Result.Failure<LoginResponseDto>(new Error("Auth.InvalidCredentials", "Invalid username or password."));
        }

        // Concurrent Login Check
        if (!string.IsNullOrEmpty(user.CurrentSessionId) && !dto.ForceLogin)
        {
            _logger.LogWarning("Concurrent login attempt for {UserName} from IP {IpAddress}. Existing session active.", dto.UserName, ipAddress);
            return Result.Failure<LoginResponseDto>(new Error("Auth.ConcurrentLogin", "User is already logged in on another device. Do you want to continue here?"));
        }

        // Start New Session
        var sessionId = Guid.NewGuid().ToString();

        // Concurrent Login Cleanup: If forcing login, remove the token from the previous session (LastKnownIp)
        if (dto.ForceLogin && !string.IsNullOrEmpty(user.LastKnownIp))
        {
             _logger.LogInformation("Forcing login for {UserName}. Revoking tokens from previous IP {LastKnownIp}", dto.UserName, user.LastKnownIp);
             user.RefreshTokens.RemoveAll(rt => rt.CreatedByIp == user.LastKnownIp);
        }

        user.CurrentSessionId = sessionId;
        user.LastLoginTime = DateTime.UtcNow;
        user.LastKnownIp = ipAddress;
        user.LastKnownUserAgent = userAgent;

        var accessToken = await GenerateJwtToken(user, sessionId);
        var refreshToken = GenerateRefreshToken(ipAddress);

        // Enforce One Token Per IP: Remove any existing active tokens for this IP
        user.RefreshTokens.RemoveAll(rt => rt.CreatedByIp == ipAddress);

        // Aggressive Cleanup: Remove ALL tokens that are expired OR revoked
        user.RefreshTokens.RemoveAll(rt => !rt.IsActive || rt.ExpiresUtc <= DateTime.UtcNow);

        user.RefreshTokens.Add(refreshToken);
        var updateResult = await UpdateUserWithConcurrencyRetryAsync(user);
        
        if (!updateResult.Succeeded)
        {
            _logger.LogError("Login failed for {UserName} during DB update. Errors: {Errors}", dto.UserName, string.Join(", ", updateResult.Errors.Select(e => e.Description)));
            return Result.Failure<LoginResponseDto>(new Error("Auth.UpdateFailed", $"Login failed: {string.Join(", ", updateResult.Errors.Select(e => e.Description))}"));
        }

        _logger.LogInformation("User {UserName} logged in successfully from IP {IpAddress}", dto.UserName, ipAddress);
        return Result.Success(new LoginResponseDto
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken.Token,
            UserName = user.UserName!
        });
    }

    public async Task<Result<LoginResponseDto>> RefreshTokenAsync(string token, string ipAddress)
    {
        var user = await _userManager.Users
            .Include(u => u.RefreshTokens)
            .SingleOrDefaultAsync(u => u.RefreshTokens.Any(t => t.Token == token));

        if (user == null)
        {
            _logger.LogWarning("Refresh token attempt failed. Token not found or user does not exist. IP: {IpAddress}", ipAddress);
            return Result.Failure<LoginResponseDto>(new Error("Auth.InvalidToken", "Invalid token."));
        }

        var refreshToken = user.RefreshTokens.Single(x => x.Token == token);

        if (!refreshToken.IsActive)
        {
            _logger.LogWarning("Refresh token attempt failed. Token is inactive/revoked. User: {UserName}, IP: {IpAddress}", user.UserName, ipAddress);
            return Result.Failure<LoginResponseDto>(new Error("Auth.InvalidToken", "Invalid token."));
        }

        // Revoke the old token (Rotation)
        refreshToken.RevokedUtc = DateTime.UtcNow;
        refreshToken.RevokedByIp = ipAddress;

        // Generate new tokens
        var newAccessToken = await GenerateJwtToken(user, user.CurrentSessionId ?? "");
        var newRefreshToken = GenerateRefreshToken(ipAddress);

        // Enforce One Token Per IP (for the new token)
        user.RefreshTokens.RemoveAll(rt => rt.CreatedByIp == ipAddress);

        // Aggressive Cleanup
        user.RefreshTokens.RemoveAll(rt => !rt.IsActive || rt.ExpiresUtc <= DateTime.UtcNow);

        user.RefreshTokens.Add(newRefreshToken);
        await UpdateUserWithConcurrencyRetryAsync(user);

        _logger.LogInformation("Token refreshed successfully for user {UserName} from IP {IpAddress}", user.UserName, ipAddress);

        return Result.Success(new LoginResponseDto
        {
            AccessToken = newAccessToken,
            RefreshToken = newRefreshToken.Token,
            UserName = user.UserName!
        });
    }

    public async Task<Result<bool>> LogoutAsync(string token, string ipAddress)
    {
        // Find the user who owns this token
        var user = await _userManager.Users
            .Include(u => u.RefreshTokens)
            .SingleOrDefaultAsync(u => u.RefreshTokens.Any(t => t.Token == token));

        if (user == null) return Result.Success(true); // Already logged out effectively

        var refreshToken = user.RefreshTokens.SingleOrDefault(x => x.Token == token);
        if (refreshToken != null && refreshToken.IsActive)
        {
            refreshToken.RevokedUtc = DateTime.UtcNow;
            refreshToken.RevokedByIp = ipAddress;
            
            // ⭐ CRITICAL: Clear session state to fully terminate unattended sessions
            user.CurrentSessionId = null;
            user.LastKnownIp = null;
            user.LastKnownUserAgent = null;
            
            await UpdateUserWithConcurrencyRetryAsync(user);
            _logger.LogInformation("Session terminated for {UserName} from IP {IpAddress}", user.UserName, ipAddress);
        }

        return Result.Success(true);
    }

    public async Task<Result<string>> ForgotPasswordAsync(string email)
    {
        var user = await _userManager.FindByEmailAsync(email);
        if (user == null)
        {
            _logger.LogInformation("Forgot password requested for non-existent email: {Email}", email);
            // Don't reveal that the user does not exist
            return Result.Success("If an account exists, a password reset email has been sent.");
        }

        var db = _redis.GetDatabase();
        var redisKey = $"otp:{user.Email}";

        // If a still-valid OTP already exists (user requesting a resend,
        // most commonly because the first email is slow to arrive), reuse
        // it rather than silently invalidating it with a new one. This is
        // the fix — every email the user has for this reset attempt, old
        // or new, will contain a code that actually still works.
        var existingOtp = await db.StringGetAsync(redisKey);
        string otp;

        if (!existingOtp.IsNullOrEmpty)
        {
            otp = existingOtp.ToString();
            // Refresh the TTL back to the full window so a resend also
            // buys the user more time, rather than counting down against
            // however much was left on the original.
            await db.KeyExpireAsync(redisKey, TimeSpan.FromMinutes(10));
            _logger.LogInformation("Reusing existing pending OTP for {Email} (resend requested).", user.Email);
        }
        else
        {
            otp = RandomNumberGenerator.GetInt32(100000, 999999).ToString();
            await db.StringSetAsync(redisKey, otp, TimeSpan.FromMinutes(10));
        }

        // Generate a beautiful HTML email template
        var htmlBody = $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8'>
    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
    <style>
        @import url('https://fonts.googleapis.com/css2?family=Outfit:wght@300;400;500;600;700&display=swap');
        body {{ margin: 0; padding: 0; font-family: 'Outfit', 'Segoe UI', Arial, sans-serif; background: linear-gradient(135deg, #0a0a0f, #111827, #0a0a0f); }}
        .wrapper {{ min-height: 100vh; padding: 40px 20px; background: linear-gradient(135deg, #0a0a0f 0%, #111827 50%, #0a0a0f 100%); }}
        .container {{ max-width: 520px; margin: 0 auto; }}
        .header {{ text-align: center; padding: 30px 0 20px; }}
        .header-icon {{ display: inline-block; width: 48px; height: 48px; background: linear-gradient(135deg, #059669, #0284c7); border-radius: 14px; line-height: 48px; font-size: 24px; margin-bottom: 12px; }}
        .header h1 {{ margin: 0; font-size: 22px; font-weight: 700; background: linear-gradient(135deg, #10b981, #38bdf8); -webkit-background-clip: text; -webkit-text-fill-color: transparent; background-clip: text; }}
        .card {{ background: rgba(255, 255, 255, 0.05); border: 1px solid rgba(255, 255, 255, 0.08); border-radius: 20px; padding: 36px 32px; backdrop-filter: blur(16px); }}
        .card p {{ color: #a1a1aa; font-size: 15px; line-height: 1.7; margin: 0 0 20px; }}
        .card p strong {{ color: #f4f4f5; }}
        .otp-container {{ text-align: center; margin: 28px 0; }}
        .otp-box {{ display: inline-block; background: linear-gradient(135deg, rgba(5, 150, 105, 0.15), rgba(2, 132, 199, 0.15)); border: 2px solid rgba(16, 185, 129, 0.3); border-radius: 16px; padding: 20px 40px; font-size: 38px; font-weight: 700; color: #10b981; letter-spacing: 8px; font-family: 'Outfit', monospace; }}
        .warning {{ background: rgba(251, 191, 36, 0.08); border: 1px solid rgba(251, 191, 36, 0.15); border-radius: 12px; padding: 14px 18px; margin-top: 24px; }}
        .warning p {{ color: #fbbf24; font-size: 13px; margin: 0; }}
        .footer {{ text-align: center; padding: 24px 0; }}
        .footer p {{ color: #52525b; font-size: 12px; margin: 0; }}
        .footer a {{ color: #10b981; text-decoration: none; }}
    </style>
</head>
<body>
    <div class='wrapper'>
        <div class='container'>
            <div class='header'>
                <div class='header-icon'>💰</div>
                <h1>Financial Planner</h1>
            </div>
            <div class='card'>
                <p style='font-size: 18px; color: #f4f4f5; font-weight: 600; margin-bottom: 8px;'>Password Reset Request</p>
                <p>Hello, we received a request to reset your password. Use the secure One-Time Password below to proceed.</p>
                <div class='otp-container'>
                    <div class='otp-box'>{otp}</div>
                </div>
                <p style='text-align: center; color: #71717a; font-size: 13px;'>This code is valid for <strong style='color: #10b981;'>10 minutes</strong></p>
                <div class='warning'>
                    <p>⚠️ If you did not request this, please ignore this email or contact support.</p>
                </div>
            </div>
            <div class='footer'>
                <p>&copy; {DateTime.UtcNow.Year} Financial Planner &bull; <a href='#'>Privacy Policy</a></p>
            </div>
        </div>
    </div>
</body>
</html>";

        // Send Email via Queue
        var mailRequest = new MailRequest
        {
            To = user.Email!,
            Subject = "Your Password Reset OTP",
            Body = htmlBody
        };

        await _emailService.SendEmailAsync(mailRequest);

        return Result.Success("If an account exists, a password reset email has been sent.");
    }

    // Shared by every OTP-checking path so the attempt limit can't
    // drift out of sync between them. Attempts are tracked in Redis
    // alongside the OTP itself, with the same lifetime — no separate
    // cleanup needed, it expires when the OTP would anyway.
    private async Task<Result<bool>> ValidateOtpWithAttemptLimitAsync(
        StackExchange.Redis.IDatabase db, string email, string providedOtp)
    {
        const int maxAttempts = 5;
        var attemptsKey = $"otp-attempts:{email}";
        var otpKey = $"otp:{email}";

        var attempts = (int)(await db.StringGetAsync(attemptsKey));

        if (attempts >= maxAttempts)
        {
            return Result.Failure<bool>(new Error(
                "Auth.TooManyAttempts", "Too many incorrect attempts. Request a new code."));
        }

        var storedOtp = await db.StringGetAsync(otpKey);

        if (storedOtp.IsNullOrEmpty || storedOtp != providedOtp)
        {
            var otpTtl = await db.KeyTimeToLiveAsync(otpKey);
            await db.StringIncrementAsync(attemptsKey);
            await db.KeyExpireAsync(attemptsKey, otpTtl ?? TimeSpan.FromMinutes(10));

            return Result.Failure<bool>(new Error("Auth.InvalidToken", "Invalid or expired OTP."));
        }

        // Correct on this attempt — the attempt counter no longer
        // serves a purpose, and OTP deletion (already handled by each
        // caller after a fully successful flow) makes it moot anyway.
        await db.KeyDeleteAsync(attemptsKey);

        return Result.Success(true);
    }

    public async Task<Result<bool>> VerifyOtpAsync(VerifyOtpDto dto)
    {
        var db = _redis.GetDatabase();
        return await ValidateOtpWithAttemptLimitAsync(db, dto.Email, dto.Otp);
    }

    public async Task<Result<bool>> ResetPasswordAsync(ResetPasswordDto dto)
    {
        var db = _redis.GetDatabase();
        var otpCheck = await ValidateOtpWithAttemptLimitAsync(db, dto.Email, dto.Otp);
        if (otpCheck.IsFailure)
            return otpCheck;

        var normalizedEmail = _userManager.NormalizeEmail(dto.Email);
        var user = await _userManager.Users
            .Include(u => u.RefreshTokens)
            .FirstOrDefaultAsync(u => u.NormalizedEmail == normalizedEmail);
        if (user == null)
        {
             return Result.Failure<bool>(new Error("Auth.InvalidToken", "Invalid user."));
        }

        var token = await _userManager.GeneratePasswordResetTokenAsync(user);
        var result = await _userManager.ResetPasswordAsync(user, token, dto.NewPassword);

        if (!result.Succeeded)
        {
            var errors = string.Join(", ", result.Errors.Select(e => e.Description));
            return Result.Failure<bool>(new Error("Auth.ResetFailed", $"Password reset failed: {errors}"));
        }

        // A successful reset should invalidate every existing session —
        // otherwise a refresh token obtained before the reset (by an
        // attacker, or on a device the real owner no longer trusts)
        // stays valid afterward, defeating the point of resetting the
        // password at all. Matches the same RemoveAll pattern already
        // used for revocation elsewhere in this file.
        user.RefreshTokens.RemoveAll(rt => true);
        user.CurrentSessionId = null;
        await UpdateUserWithConcurrencyRetryAsync(user);

        // Invalidate OTP
        await db.KeyDeleteAsync($"otp:{dto.Email}");

        return Result.Success(true);
    }

    public async Task<Result<UserProfileDto>> UpdateProfileAsync(string userId, UpdateProfileDto dto)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user == null)
            return Result.Failure<UserProfileDto>(new Error("User.NotFound", "User not found."));

        user.Name = dto.Name;
        user.OverspendAlertsEnabled = dto.OverspendAlertsEnabled;

        var wantsEmailChange = !string.IsNullOrWhiteSpace(dto.NewEmail) &&
                                !dto.NewEmail.Equals(user.Email, StringComparison.OrdinalIgnoreCase);

        if (wantsEmailChange)
        {
            if (string.IsNullOrWhiteSpace(dto.CurrentPassword) ||
                !await _userManager.CheckPasswordAsync(user, dto.CurrentPassword))
            {
                return Result.Failure<UserProfileDto>(new Error(
                    "Auth.InvalidPassword",
                    "Your current password is required and must be correct to change your email."));
            }

            var existingUserWithEmail = await _userManager.FindByEmailAsync(dto.NewEmail!);
            if (existingUserWithEmail != null && existingUserWithEmail.Id != userId)
                return Result.Failure<UserProfileDto>(new Error("Auth.EmailInUse", "This email is already in use."));

            user.Email = dto.NewEmail;
            user.UserName = dto.NewEmail;
            user.EmailConfirmed = false;
        }

        var updateResult = await UpdateUserWithConcurrencyRetryAsync(user);
        if (!updateResult.Succeeded)
        {
            var errors = string.Join(", ", updateResult.Errors.Select(e => e.Description));
            return Result.Failure<UserProfileDto>(new Error("Profile.UpdateFailed", errors));
        }

        return Result.Success(new UserProfileDto
        {
            Name = user.Name,
            Email = user.Email!,
            OverspendAlertsEnabled = user.OverspendAlertsEnabled
        });
    }

    private async Task<string> GenerateJwtToken(ApplicationUser user, string sessionId)
    {
        var jwtKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtSettings.Key));
        var credentials = new SigningCredentials(jwtKey, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id),
            new Claim(JwtRegisteredClaimNames.Sub, user.UserName!),
            new Claim(JwtRegisteredClaimNames.Email, user.Email!),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new Claim("SessionId", sessionId)
        };

        // Add role claims so [Authorize(Roles = "Admin")] works
        var roles = await _userManager.GetRolesAsync(user);
        foreach (var role in roles)
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
        }

        var token = new JwtSecurityToken(
            issuer: _jwtSettings.Issuer,
            audience: _jwtSettings.Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(_jwtSettings.DurationInMinutes),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private RefreshToken GenerateRefreshToken(string ipAddress)
    {
        var randomNumber = new byte[32];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(randomNumber);

        return new RefreshToken
        {
            Token = Convert.ToBase64String(randomNumber),
            ExpiresUtc = DateTime.UtcNow.AddDays(_jwtSettings.RefreshTokenTTL), // Long life
            CreatedUtc = DateTime.UtcNow,
            CreatedByIp = ipAddress
        };
    }

    private async Task<IdentityResult> UpdateUserWithConcurrencyRetryAsync(ApplicationUser user)
    {
        var updateResult = await _userManager.UpdateAsync(user);
        if (!updateResult.Succeeded && updateResult.Errors.Any(e => e.Code == "ConcurrencyFailure" || e.Description.Contains("Optimistic concurrency")))
        {
            _logger.LogWarning("Concurrency conflict detected for user {UserId}. Fetching latest ConcurrencyStamp and retrying...", user.Id);

            var latestStamp = await _userManager.Users
                .AsNoTracking()
                .Where(u => u.Id == user.Id)
                .Select(u => u.ConcurrencyStamp)
                .FirstOrDefaultAsync();

            if (latestStamp != null)
            {
                user.ConcurrencyStamp = latestStamp;
                updateResult = await _userManager.UpdateAsync(user);
            }
        }
        return updateResult;
    }
}