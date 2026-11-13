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
    // Lazy: the Redis connection is opened on the first OTP request, not for every login.
    private readonly Lazy<IConnectionMultiplexer> _redis;
    private readonly IEmailService _emailService;
    private readonly SessionSecuritySettings _sessionSettings;

    public AuthService(UserManager<ApplicationUser> userManager, IOptions<JwtSettings> jwtOptions, ILogger<AuthService> logger, Lazy<IConnectionMultiplexer> redis, IEmailService emailService, IOptions<SessionSecuritySettings> sessionOptions)
    {
        _userManager = userManager;
        _jwtSettings = jwtOptions.Value;
        _sessionSettings = sessionOptions.Value;
        _logger = logger;
        _redis = redis;
        _emailService = emailService;
    }

    // ── OTP storage: only a keyed hash of each code is ever kept in Redis ─────────────
    // A Redis dump, log or support screenshot can no longer reveal a live code. The hash is an
    // HMAC (server secret + purpose + email + code), so it can't be precomputed or reused across
    // purposes. Comparison is constant-time.
    private string HashOtp(string purpose, string email, string otp)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_jwtSettings.Key));
        var payload = $"{purpose}|{email.Trim().ToLowerInvariant()}|{otp}";
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(payload)));
    }

    private static bool HashesEqual(string a, string b)
        => CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));

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
        var db = _redis.Value.GetDatabase();
        var registrationData = System.Text.Json.JsonSerializer.Serialize(dto);
        await db.StringSetAsync($"reg:{dto.Email}", registrationData, TimeSpan.FromMinutes(10));
        await db.StringSetAsync($"reg-otp:{dto.Email}", HashOtp("reg", dto.Email, otp), TimeSpan.FromMinutes(10));

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
        var db = _redis.Value.GetDatabase();

        // Verify OTP — with the same 5-attempt cap the password-reset flow already has.
        // Without it a 6-digit code could be brute-forced within its 10-minute lifetime.
        var regOtpKey = $"reg-otp:{dto.Email}";
        var regAttemptsKey = $"reg-otp-attempts:{dto.Email}";

        var regAttempts = (int)(await db.StringGetAsync(regAttemptsKey));
        if (regAttempts >= 5)
        {
            await db.KeyDeleteAsync(regOtpKey); // force a fresh code
            return Result.Failure<string>(new Error("Auth.TooManyAttempts", "Too many incorrect attempts. Please register again to get a new code."));
        }

        var storedOtp = await db.StringGetAsync(regOtpKey);
        if (storedOtp.IsNullOrEmpty || !HashesEqual(storedOtp.ToString(), HashOtp("reg", dto.Email, dto.Otp ?? string.Empty)))
        {
            var regTtl = await db.KeyTimeToLiveAsync(regOtpKey);
            await db.StringIncrementAsync(regAttemptsKey);
            await db.KeyExpireAsync(regAttemptsKey, regTtl ?? TimeSpan.FromMinutes(10));
            return Result.Failure<string>(new Error("Auth.InvalidOtp", "Invalid or expired verification code."));
        }
        await db.KeyDeleteAsync(regAttemptsKey);

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

    // ── Sessions & devices ───────────────────────────────────────────────────────────
    // A session = one sign-in on one device. All refresh tokens rotated from the same login share a
    // SessionId (also the "SessionId" claim in the access token). Several sessions can be live at
    // once; the user sees them in Settings and can sign any of them out.
    //
    // Revoked tokens are kept for a day so that a token replayed after it was rotated is recognised
    // as possible theft (and its whole session is signed out), instead of just looking unknown.
    private static readonly TimeSpan RevokedTokenRetention = TimeSpan.FromDays(1);
    private const int MaxDevicesPerUser = 20;
    private const int MaxUserAgentLength = 512;

    private static string? Truncate(string? value, int max)
        => value is { Length: > 0 } && value.Length > max ? value[..max] : value;

    private static void PruneTokens(ApplicationUser user)
        => user.RefreshTokens.RemoveAll(rt =>
            rt.ExpiresUtc <= DateTime.UtcNow ||
            (rt.RevokedUtc != null && rt.RevokedUtc < DateTime.UtcNow - RevokedTokenRetention));

    private static void RevokeSessionTokens(ApplicationUser user, string sessionId, string ipAddress)
    {
        foreach (var t in user.RefreshTokens.Where(t => t.SessionId == sessionId && t.RevokedUtc == null))
        {
            t.RevokedUtc = DateTime.UtcNow;
            t.RevokedByIp = ipAddress;
        }
        if (user.CurrentSessionId == sessionId)
            user.CurrentSessionId = null;
    }

    public async Task<Result<LoginResponseDto>> LoginAsync(LoginUserDto dto, string ipAddress, string userAgent, string deviceId)
    {
        var user = await _userManager.Users
            .Include(u => u.RefreshTokens) // Important: Load existing tokens
            .Include(u => u.Devices)
            .FirstOrDefaultAsync(u => u.UserName == dto.UserName);

        // Lockout was configured (5 attempts / 15 min) but never enforced, because plain
        // CheckPasswordAsync does not count failures. Count them explicitly.
        if (user != null && await _userManager.IsLockedOutAsync(user))
        {
            _logger.LogWarning("Login attempt on locked-out account {UserName} from IP {IpAddress}", dto.UserName, ipAddress);
            return Result.Failure<LoginResponseDto>(new Error("Auth.LockedOut", "Too many failed attempts. Please try again in a few minutes."));
        }

        if (user == null || !await _userManager.CheckPasswordAsync(user, dto.Password))
        {
            if (user != null)
                await _userManager.AccessFailedAsync(user);

            _logger.LogWarning("Failed login attempt for {UserName} from IP {IpAddress}", dto.UserName, ipAddress);
            return Result.Failure<LoginResponseDto>(new Error("Auth.InvalidCredentials", "Invalid username or password."));
        }

        if (user.AccessFailedCount > 0)
            await _userManager.ResetAccessFailedCountAsync(user);

        // A user's very first device is the baseline, not "new". After that, a device id we have not
        // seen for this user is a new device.
        var isNewDevice = user.Devices.Count > 0 && user.Devices.All(d => d.DeviceId != deviceId);

        if (isNewDevice && _sessionSettings.RequireCodeOnNewDevice)
        {
            var verification = await VerifyNewDeviceCodeAsync(user, deviceId, dto.DeviceVerificationCode, userAgent, ipAddress);
            if (verification.IsFailure)
                return Result.Failure<LoginResponseDto>(verification.Error);
        }

        var now = DateTime.UtcNow;
        var sessionId = Guid.NewGuid().ToString();

        // Signing in again on the same device replaces that device's old session, so the device
        // list does not fill up with duplicates.
        var sameDeviceSessions = user.RefreshTokens
            .Where(t => t.IsActive && t.DeviceId == deviceId && t.SessionId != null)
            .Select(t => t.SessionId!)
            .Distinct()
            .ToList();
        foreach (var oldSession in sameDeviceSessions)
            RevokeSessionTokens(user, oldSession, ipAddress);

        PruneTokens(user);

        // Cap the number of live sessions: sign out the least recently active ones.
        var maxSessions = Math.Max(1, _sessionSettings.MaxSessionsPerUser);
        var liveSessions = user.RefreshTokens
            .Where(t => t.IsActive && t.SessionId != null)
            .GroupBy(t => t.SessionId!)
            .Select(g => new { Id = g.Key, LastActive = g.Max(t => t.CreatedUtc) })
            .OrderBy(s => s.LastActive)
            .ToList();
        for (var i = 0; i < liveSessions.Count - (maxSessions - 1); i++)
            RevokeSessionTokens(user, liveSessions[i].Id, ipAddress);

        // Remember this device.
        var cappedUserAgent = Truncate(userAgent, MaxUserAgentLength);
        var device = user.Devices.FirstOrDefault(d => d.DeviceId == deviceId);
        if (device == null)
        {
            user.Devices.Add(new UserDevice
            {
                DeviceId = deviceId,
                FirstSeenUtc = now,
                LastSeenUtc = now,
                LastIp = ipAddress,
                UserAgent = cappedUserAgent
            });
        }
        else
        {
            device.LastSeenUtc = now;
            device.LastIp = ipAddress;
            device.UserAgent = cappedUserAgent;
        }
        while (user.Devices.Count > MaxDevicesPerUser)
            user.Devices.Remove(user.Devices.OrderBy(d => d.LastSeenUtc).First());

        // Latest-login audit fields (kept for the admin views; not used to decide anything now).
        user.CurrentSessionId = sessionId;
        user.LastLoginTime = now;
        user.LastSeenUtc = now;
        user.LastKnownIp = ipAddress;
        user.LastKnownUserAgent = cappedUserAgent;

        var accessToken = await GenerateJwtToken(user, sessionId);
        var refreshToken = GenerateRefreshToken(ipAddress, sessionId, now, deviceId, cappedUserAgent);
        user.RefreshTokens.Add(refreshToken);

        var updateResult = await UpdateUserWithConcurrencyRetryAsync(user);

        if (!updateResult.Succeeded)
        {
            _logger.LogError("Login failed for {UserName} during DB update. Errors: {Errors}", dto.UserName, string.Join(", ", updateResult.Errors.Select(e => e.Description)));
            return Result.Failure<LoginResponseDto>(new Error("Auth.UpdateFailed", $"Login failed: {string.Join(", ", updateResult.Errors.Select(e => e.Description))}"));
        }

        // Tell the user about a new device. When a code was required the code email already did.
        if (isNewDevice && _sessionSettings.NotifyOnNewDevice && !_sessionSettings.RequireCodeOnNewDevice)
            await SendNewDeviceEmailAsync(user, userAgent, ipAddress, now);

        _logger.LogInformation("User {UserName} logged in from IP {IpAddress} (session {SessionId}, new device: {IsNewDevice})",
            dto.UserName, ipAddress, sessionId, isNewDevice);
        return Result.Success(new LoginResponseDto
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken.Token,
            UserName = user.UserName!
        });
    }

    public async Task<Result<LoginResponseDto>> RefreshTokenAsync(string token, string ipAddress, string deviceId)
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

        if (refreshToken.RevokedUtc != null)
        {
            // A revoked token came back. Within a few seconds that is two tabs racing to refresh;
            // later than that it is a replay (for example a stolen cookie), so sign the whole
            // session out: whoever holds the newer token loses it too.
            var graceSeconds = Math.Max(0, _sessionSettings.RefreshReuseGraceSeconds);
            if (DateTime.UtcNow - refreshToken.RevokedUtc.Value > TimeSpan.FromSeconds(graceSeconds)
                && refreshToken.SessionId != null)
            {
                _logger.LogWarning("Refresh token reuse detected for {UserName} (session {SessionId}) from IP {IpAddress}. Signing the session out.",
                    user.UserName, refreshToken.SessionId, ipAddress);
                RevokeSessionTokens(user, refreshToken.SessionId, ipAddress);
                await UpdateUserWithConcurrencyRetryAsync(user);
            }
            else
            {
                _logger.LogWarning("Refresh token attempt failed. Token is revoked. User: {UserName}, IP: {IpAddress}", user.UserName, ipAddress);
            }
            return Result.Failure<LoginResponseDto>(new Error("Auth.InvalidToken", "Invalid token."));
        }

        if (refreshToken.IsExpired)
        {
            _logger.LogWarning("Refresh token attempt failed. Token expired. User: {UserName}, IP: {IpAddress}", user.UserName, ipAddress);
            return Result.Failure<LoginResponseDto>(new Error("Auth.InvalidToken", "Invalid token."));
        }

        // The token only works together with the device cookie it was issued for. Tokens issued
        // before this feature have no device yet and are bound on first use.
        if (!string.IsNullOrEmpty(refreshToken.DeviceId) &&
            !string.Equals(refreshToken.DeviceId, deviceId, StringComparison.Ordinal))
        {
            _logger.LogWarning("Refresh token for {UserName} presented without its device cookie from IP {IpAddress}. Rejected.", user.UserName, ipAddress);
            return Result.Failure<LoginResponseDto>(new Error("Auth.InvalidToken", "Invalid token."));
        }

        var now = DateTime.UtcNow;
        var sessionId = refreshToken.SessionId ?? user.CurrentSessionId ?? Guid.NewGuid().ToString();

        // Proof the session is still in use.
        user.LastSeenUtc = now;

        // Revoke the old token (Rotation)
        refreshToken.RevokedUtc = now;
        refreshToken.RevokedByIp = ipAddress;

        // Generate new tokens, in the same session on the same device
        var newAccessToken = await GenerateJwtToken(user, sessionId);
        var newRefreshToken = GenerateRefreshToken(ipAddress, sessionId,
            refreshToken.SessionStartedUtc ?? refreshToken.CreatedUtc, deviceId, refreshToken.UserAgent);

        PruneTokens(user);

        user.RefreshTokens.Add(newRefreshToken);
        var persistResult = await UpdateUserWithConcurrencyRetryAsync(user);
        if (!persistResult.Succeeded)
        {
            // Never hand out a refresh token that was not stored: it could not be used (or rotated) later.
            _logger.LogError("Refresh failed for {UserName}: could not persist the rotated token. Errors: {Errors}",
                user.UserName, string.Join(", ", persistResult.Errors.Select(e => e.Description)));
            return Result.Failure<LoginResponseDto>(new Error("Auth.RefreshFailed", "Could not refresh your session. Please sign in again."));
        }

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
            // Sign out this device only (every token of its session); other devices stay signed in.
            if (refreshToken.SessionId != null)
            {
                RevokeSessionTokens(user, refreshToken.SessionId, ipAddress);
            }
            else
            {
                refreshToken.RevokedUtc = DateTime.UtcNow;
                refreshToken.RevokedByIp = ipAddress;
            }

            await UpdateUserWithConcurrencyRetryAsync(user);
            _logger.LogInformation("Session terminated for {UserName} from IP {IpAddress}", user.UserName, ipAddress);
        }

        return Result.Success(true);
    }

    // ── Session list (Settings > Devices) ────────────────────────────────────────────

    public async Task<Result<List<SessionDto>>> GetSessionsAsync(string userId, string? currentSessionId)
    {
        var now = DateTime.UtcNow;
        var tokens = await _userManager.Users
            .Where(u => u.Id == userId)
            .SelectMany(u => u.RefreshTokens)
            .Where(t => t.RevokedUtc == null && t.ExpiresUtc > now && t.SessionId != null)
            .Select(t => new { t.SessionId, t.SessionStartedUtc, t.CreatedUtc, t.CreatedByIp, t.UserAgent })
            .ToListAsync();

        var sessions = tokens
            .GroupBy(t => t.SessionId!)
            .Select(g =>
            {
                var latest = g.OrderByDescending(t => t.CreatedUtc).First();
                return new SessionDto
                {
                    SessionId = g.Key,
                    Device = Application.Common.Helpers.DeviceInfo.Describe(latest.UserAgent),
                    IpAddress = latest.CreatedByIp,
                    SignedInUtc = g.Min(t => t.SessionStartedUtc ?? t.CreatedUtc),
                    LastActiveUtc = latest.CreatedUtc,
                    IsCurrent = g.Key == currentSessionId
                };
            })
            .OrderByDescending(s => s.IsCurrent)
            .ThenByDescending(s => s.LastActiveUtc)
            .ToList();

        return Result.Success(sessions);
    }

    public async Task<Result<bool>> RevokeSessionAsync(string userId, string sessionId, string ipAddress)
    {
        var user = await _userManager.Users
            .Include(u => u.RefreshTokens)
            .FirstOrDefaultAsync(u => u.Id == userId);
        if (user == null)
            return Result.Failure<bool>(new Error("User.NotFound", "User not found."));

        if (!user.RefreshTokens.Any(t => t.SessionId == sessionId && t.IsActive))
            return Result.Failure<bool>(new Error("Session.NotFound", "That device is no longer signed in."));

        RevokeSessionTokens(user, sessionId, ipAddress);
        await UpdateUserWithConcurrencyRetryAsync(user);
        _logger.LogInformation("Session {SessionId} signed out by user {UserId}", sessionId, userId);
        return Result.Success(true);
    }

    public async Task<Result<bool>> RevokeOtherSessionsAsync(string userId, string? currentSessionId, string ipAddress)
    {
        var user = await _userManager.Users
            .Include(u => u.RefreshTokens)
            .FirstOrDefaultAsync(u => u.Id == userId);
        if (user == null)
            return Result.Failure<bool>(new Error("User.NotFound", "User not found."));

        var others = user.RefreshTokens
            .Where(t => t.IsActive && t.SessionId != null && t.SessionId != currentSessionId)
            .Select(t => t.SessionId!)
            .Distinct()
            .ToList();
        foreach (var sid in others)
            RevokeSessionTokens(user, sid, ipAddress);

        if (others.Count > 0)
            await UpdateUserWithConcurrencyRetryAsync(user);
        _logger.LogInformation("User {UserId} signed out {Count} other session(s)", userId, others.Count);
        return Result.Success(true);
    }

    // ── New-device verification code (only when Security:Sessions:RequireCodeOnNewDevice is on) ──

    private async Task<Result<bool>> VerifyNewDeviceCodeAsync(
        ApplicationUser user, string deviceId, string? code, string userAgent, string ipAddress)
    {
        var db = _redis.Value.GetDatabase();
        var key = $"dev-otp:{user.Id}:{deviceId}";
        var attemptsKey = $"dev-otp-attempts:{user.Id}:{deviceId}";
        var cooldownKey = $"dev-otp-cool:{user.Id}:{deviceId}";
        var purpose = $"device:{deviceId}";

        if (string.IsNullOrWhiteSpace(code))
        {
            // First step: email a code (at most one a minute), then ask the client to collect it.
            if (!await db.KeyExistsAsync(cooldownKey))
            {
                var otp = RandomNumberGenerator.GetInt32(100000, 999999).ToString();
                await db.StringSetAsync(key, HashOtp(purpose, user.Email!, otp), TimeSpan.FromMinutes(10));
                await db.KeyDeleteAsync(attemptsKey);
                await db.StringSetAsync(cooldownKey, "1", TimeSpan.FromSeconds(60));
                await SendNewDeviceCodeEmailAsync(user, otp, userAgent, ipAddress);
            }
            return Result.Failure<bool>(new Error("Auth.DeviceVerificationRequired",
                "We emailed a verification code to confirm this new device. Enter it to continue."));
        }

        var attempts = (int)(await db.StringGetAsync(attemptsKey));
        if (attempts >= 5)
        {
            await db.KeyDeleteAsync(key); // force a fresh code
            return Result.Failure<bool>(new Error("Auth.TooManyAttempts", "Too many incorrect attempts. Sign in again to get a new code."));
        }

        var stored = await db.StringGetAsync(key);
        if (stored.IsNullOrEmpty || !HashesEqual(stored.ToString(), HashOtp(purpose, user.Email!, code.Trim())))
        {
            var ttl = await db.KeyTimeToLiveAsync(key);
            await db.StringIncrementAsync(attemptsKey);
            await db.KeyExpireAsync(attemptsKey, ttl ?? TimeSpan.FromMinutes(10));
            return Result.Failure<bool>(new Error("Auth.InvalidOtp", "Invalid or expired verification code."));
        }

        await db.KeyDeleteAsync(key);
        await db.KeyDeleteAsync(attemptsKey);
        await db.KeyDeleteAsync(cooldownKey);
        return Result.Success(true);
    }

    // ── Security emails ──────────────────────────────────────────────────────────────

    private static string SecurityEmailShell(string heading, string bodyHtml) => $@"
<!DOCTYPE html>
<html>
<head><meta charset='utf-8'><meta name='viewport' content='width=device-width, initial-scale=1.0'></head>
<body style=""margin:0;padding:0;background:#0a0a0f;font-family:'Segoe UI',Arial,sans-serif;"">
  <div style='max-width:520px;margin:0 auto;padding:40px 20px;'>
    <div style='background:#111827;border:1px solid #1f2937;border-radius:20px;padding:32px;'>
      <p style='margin:0 0 4px;color:#10b981;font-size:12px;font-weight:700;letter-spacing:2px;text-transform:uppercase;'>Financial Planner</p>
      <p style='margin:0 0 16px;color:#f4f4f5;font-size:20px;font-weight:700;'>{heading}</p>
      {bodyHtml}
    </div>
    <p style='text-align:center;color:#52525b;font-size:12px;margin:20px 0 0;'>&copy; {DateTime.UtcNow.Year} Financial Planner</p>
  </div>
</body>
</html>";

    private static string DeviceDetailsHtml(string userAgent, string ipAddress, DateTime whenUtc)
    {
        var device = System.Net.WebUtility.HtmlEncode(Application.Common.Helpers.DeviceInfo.Describe(userAgent));
        var ip = System.Net.WebUtility.HtmlEncode(ipAddress);
        return $@"<table style='color:#d4d4d8;font-size:14px;line-height:1.8;margin:0 0 16px;'>
        <tr><td style='color:#71717a;padding-right:16px;'>Device</td><td>{device}</td></tr>
        <tr><td style='color:#71717a;padding-right:16px;'>IP address</td><td>{ip}</td></tr>
        <tr><td style='color:#71717a;padding-right:16px;'>Time</td><td>{whenUtc:yyyy-MM-dd HH:mm} UTC</td></tr>
      </table>";
    }

    private async Task SendNewDeviceEmailAsync(ApplicationUser user, string userAgent, string ipAddress, DateTime whenUtc)
    {
        try
        {
            var body = $@"<p style='color:#a1a1aa;font-size:15px;line-height:1.7;margin:0 0 16px;'>Hello {System.Net.WebUtility.HtmlEncode(user.Name)}, someone signed in to your account from a device we have not seen before.</p>
      {DeviceDetailsHtml(userAgent, ipAddress, whenUtc)}
      <p style='color:#a1a1aa;font-size:14px;line-height:1.7;margin:0 0 12px;'>If this was you, you do not need to do anything.</p>
      <p style='color:#fbbf24;font-size:14px;line-height:1.7;margin:0;'>If it was not you, use <strong>Forgot password</strong> on the sign-in page right away. Resetting your password signs out every device.</p>";

            await _emailService.SendEmailAsync(new MailRequest
            {
                To = user.Email!,
                Subject = "New sign-in to your Financial Planner account",
                Body = SecurityEmailShell("New device sign-in", body)
            });
        }
        catch (Exception ex)
        {
            // A mail problem must never stop a valid login.
            _logger.LogWarning(ex, "Could not send the new-device email to user {UserId}", user.Id);
        }
    }

    private async Task SendNewDeviceCodeEmailAsync(ApplicationUser user, string otp, string userAgent, string ipAddress)
    {
        var body = $@"<p style='color:#a1a1aa;font-size:15px;line-height:1.7;margin:0 0 16px;'>Hello {System.Net.WebUtility.HtmlEncode(user.Name)}, a sign-in from a new device needs your confirmation. Enter this code to continue:</p>
      <p style='text-align:center;margin:24px 0;'><span style='display:inline-block;background:#052e2b;border:2px solid #10b981;border-radius:14px;padding:16px 32px;font-size:34px;font-weight:700;color:#10b981;letter-spacing:8px;'>{otp}</span></p>
      {DeviceDetailsHtml(userAgent, ipAddress, DateTime.UtcNow)}
      <p style='color:#a1a1aa;font-size:13px;line-height:1.7;margin:0 0 8px;'>The code is valid for 10 minutes.</p>
      <p style='color:#fbbf24;font-size:14px;line-height:1.7;margin:0;'>If this was not you, do not share the code, and reset your password.</p>";

        await _emailService.SendEmailAsync(new MailRequest
        {
            To = user.Email!,
            Subject = "Confirm your new device — Financial Planner",
            Body = SecurityEmailShell("Confirm a new device", body)
        });
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

        var db = _redis.Value.GetDatabase();
        var redisKey = $"otp-set:{user.Email!.ToLowerInvariant()}";

        // Every resend creates a NEW code and the earlier ones stay valid until the window ends
        // (a slow first email must still work). Only hashes are stored, so a code can't be
        // "reused" any more — each email simply carries its own working code. Capped at 5 live
        // codes so repeated resends can't widen the guessing surface.
        var otp = RandomNumberGenerator.GetInt32(100000, 999999).ToString();
        var otpHash = HashOtp("reset", user.Email!, otp);
        if (await db.SetLengthAsync(redisKey) >= 5)
            await db.KeyDeleteAsync(redisKey);
        await db.SetAddAsync(redisKey, otpHash);
        await db.KeyExpireAsync(redisKey, TimeSpan.FromMinutes(10));

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
        var emailKey = email.Trim().ToLowerInvariant();
        var attemptsKey = $"otp-attempts:{emailKey}";
        var otpKey = $"otp-set:{emailKey}";

        var attempts = (int)(await db.StringGetAsync(attemptsKey));

        if (attempts >= maxAttempts)
        {
            return Result.Failure<bool>(new Error(
                "Auth.TooManyAttempts", "Too many incorrect attempts. Request a new code."));
        }

        var providedHash = HashOtp("reset", email, providedOtp ?? string.Empty);

        if (!await db.SetContainsAsync(otpKey, providedHash))
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
        var db = _redis.Value.GetDatabase();
        return await ValidateOtpWithAttemptLimitAsync(db, dto.Email, dto.Otp);
    }

    public async Task<Result<bool>> ResetPasswordAsync(ResetPasswordDto dto)
    {
        var db = _redis.Value.GetDatabase();
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
        await db.KeyDeleteAsync($"otp-set:{dto.Email.Trim().ToLowerInvariant()}");

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

    private RefreshToken GenerateRefreshToken(string ipAddress, string sessionId, DateTime sessionStartedUtc, string deviceId, string? userAgent)
    {
        var randomNumber = new byte[32];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(randomNumber);

        return new RefreshToken
        {
            Token = Convert.ToBase64String(randomNumber),
            ExpiresUtc = DateTime.UtcNow.AddDays(_jwtSettings.RefreshTokenTTL), // Long life
            CreatedUtc = DateTime.UtcNow,
            CreatedByIp = ipAddress,
            SessionId = sessionId,
            SessionStartedUtc = sessionStartedUtc,
            DeviceId = deviceId,
            UserAgent = userAgent
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