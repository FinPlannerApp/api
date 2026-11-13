using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Application.Common.Helpers;
using Application.Contracts;
using Application.DTOs.Auth;
using Application.Services;
using Application.Tests.Fixtures;
using Domain.Entities;
using FluentAssertions;
using Infrastructure.Persistence;
using Infrastructure.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using Xunit;

namespace Application.Tests.Services;

/// <summary>Multi-session behaviour of AuthService: device list, rotation, reuse detection, new-device mail.</summary>
public class AuthSessionTests
{
    private const string Password = "SecurePass123!";
    private const string ChromeWin = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0 Safari/537.36";
    private const string SafariIos = "Mozilla/5.0 (iPhone; CPU iPhone OS 17_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.0 Mobile/15E148 Safari/604.1";

    private sealed class FakeEmail : IEmailService
    {
        public List<MailRequest> Sent { get; } = new();
        public Task SendEmailAsync(MailRequest mailRequest) { Sent.Add(mailRequest); return Task.CompletedTask; }
    }

    private sealed class Rig
    {
        public required ApplicationDbContext Context { get; init; }
        public required AuthService Auth { get; init; }
        public required FakeEmail Email { get; init; }
        public required string UserId { get; init; }
    }

    private static async Task<Rig> CreateRigAsync(SessionSecuritySettings? settings = null)
    {
        var context = TestDbContextFactory.Create();
        var userManager = new UserManager<ApplicationUser>(
            new UserStore<ApplicationUser, IdentityRole, ApplicationDbContext>(context),
            Options.Create(new IdentityOptions()),
            new Argon2PasswordHasher<ApplicationUser>(),
            Array.Empty<IUserValidator<ApplicationUser>>(),
            Array.Empty<IPasswordValidator<ApplicationUser>>(),
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            null!,
            NullLogger<UserManager<ApplicationUser>>.Instance);

        var user = new ApplicationUser { Name = "Tester", UserName = "tester", Email = "tester@example.com" };
        (await userManager.CreateAsync(user, Password)).Succeeded.Should().BeTrue();

        var email = new FakeEmail();
        var jwt = Options.Create(new JwtSettings
        {
            Key = new string('k', 48), Issuer = "test", Audience = "test", DurationInMinutes = 15, RefreshTokenTTL = 7
        });
        var auth = new AuthService(
            userManager, jwt, NullLogger<AuthService>.Instance,
            new Lazy<IConnectionMultiplexer>(() => throw new InvalidOperationException("Redis is not used in these tests")),
            email,
            Options.Create(settings ?? new SessionSecuritySettings()));

        return new Rig { Context = context, Auth = auth, Email = email, UserId = user.Id };
    }

    private static LoginUserDto Creds() => new() { UserName = "tester", Password = Password };

    private static string Device() => Guid.NewGuid().ToString("N");

    // ── Several devices at once ───────────────────────────────────────────────────

    [Fact]
    public async Task Two_devices_can_be_signed_in_at_the_same_time()
    {
        var rig = await CreateRigAsync();
        var laptop = Device();
        var phone = Device();

        var first = await rig.Auth.LoginAsync(Creds(), "1.1.1.1", ChromeWin, laptop);
        var second = await rig.Auth.LoginAsync(Creds(), "2.2.2.2", SafariIos, phone);

        first.IsSuccess.Should().BeTrue();
        second.IsSuccess.Should().BeTrue("a second device must not be blocked or bump the first one");

        var sessions = (await rig.Auth.GetSessionsAsync(rig.UserId, null)).Value;
        sessions.Should().HaveCount(2);
        sessions.Select(s => s.Device).Should().BeEquivalentTo("Chrome on Windows", "Safari on iOS");

        // The first device's refresh token still works.
        (await rig.Auth.RefreshTokenAsync(first.Value.RefreshToken!, "1.1.1.1", laptop)).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Signing_in_again_on_the_same_device_replaces_its_old_session()
    {
        var rig = await CreateRigAsync();
        var laptop = Device();

        var a = await rig.Auth.LoginAsync(Creds(), "1.1.1.1", ChromeWin, laptop);
        var b = await rig.Auth.LoginAsync(Creds(), "1.1.1.1", ChromeWin, laptop);

        (await rig.Auth.GetSessionsAsync(rig.UserId, null)).Value.Should().HaveCount(1);
        (await rig.Auth.RefreshTokenAsync(a.Value.RefreshToken!, "1.1.1.1", laptop)).IsSuccess.Should().BeFalse();
        (await rig.Auth.RefreshTokenAsync(b.Value.RefreshToken!, "1.1.1.1", laptop)).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Logout_signs_out_only_that_device()
    {
        var rig = await CreateRigAsync();
        var laptop = Device();
        var phone = Device();
        var a = await rig.Auth.LoginAsync(Creds(), "1.1.1.1", ChromeWin, laptop);
        var b = await rig.Auth.LoginAsync(Creds(), "2.2.2.2", SafariIos, phone);

        await rig.Auth.LogoutAsync(a.Value.RefreshToken!, "1.1.1.1");

        var sessions = (await rig.Auth.GetSessionsAsync(rig.UserId, null)).Value;
        sessions.Should().ContainSingle().Which.Device.Should().Be("Safari on iOS");
        (await rig.Auth.RefreshTokenAsync(b.Value.RefreshToken!, "2.2.2.2", phone)).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Revoke_others_keeps_only_the_current_session()
    {
        var rig = await CreateRigAsync();
        await rig.Auth.LoginAsync(Creds(), "1.1.1.1", ChromeWin, Device());
        await rig.Auth.LoginAsync(Creds(), "2.2.2.2", SafariIos, Device());
        await rig.Auth.LoginAsync(Creds(), "3.3.3.3", ChromeWin, Device());

        var current = (await rig.Auth.GetSessionsAsync(rig.UserId, null)).Value.First().SessionId;
        await rig.Auth.RevokeOtherSessionsAsync(rig.UserId, current, "9.9.9.9");

        var left = (await rig.Auth.GetSessionsAsync(rig.UserId, current)).Value;
        left.Should().ContainSingle();
        left[0].SessionId.Should().Be(current);
        left[0].IsCurrent.Should().BeTrue();
    }

    [Fact]
    public async Task A_single_session_can_be_revoked_by_id()
    {
        var rig = await CreateRigAsync();
        var a = await rig.Auth.LoginAsync(Creds(), "1.1.1.1", ChromeWin, Device());
        await rig.Auth.LoginAsync(Creds(), "2.2.2.2", SafariIos, Device());

        var target = (await rig.Auth.GetSessionsAsync(rig.UserId, null)).Value
            .Single(s => s.Device == "Chrome on Windows").SessionId;
        (await rig.Auth.RevokeSessionAsync(rig.UserId, target, "9.9.9.9")).IsSuccess.Should().BeTrue();

        (await rig.Auth.GetSessionsAsync(rig.UserId, null)).Value.Should().ContainSingle()
            .Which.Device.Should().Be("Safari on iOS");
        (await rig.Auth.RevokeSessionAsync(rig.UserId, target, "9.9.9.9")).IsSuccess.Should().BeFalse("it is already signed out");
        a.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Signing_in_beyond_the_cap_signs_out_the_least_recently_active_device()
    {
        var rig = await CreateRigAsync(new SessionSecuritySettings { MaxSessionsPerUser = 2, NotifyOnNewDevice = false });
        var d1 = Device();
        var first = await rig.Auth.LoginAsync(Creds(), "1.1.1.1", ChromeWin, d1);
        await rig.Auth.LoginAsync(Creds(), "2.2.2.2", SafariIos, Device());
        await rig.Auth.LoginAsync(Creds(), "3.3.3.3", ChromeWin, Device());

        (await rig.Auth.GetSessionsAsync(rig.UserId, null)).Value.Should().HaveCount(2);
        (await rig.Auth.RefreshTokenAsync(first.Value.RefreshToken!, "1.1.1.1", d1)).IsSuccess.Should().BeFalse();
    }

    // ── Token rotation, device binding, reuse detection ───────────────────────────

    [Fact]
    public async Task Refresh_rotates_the_token_inside_the_same_session()
    {
        var rig = await CreateRigAsync();
        var laptop = Device();
        var login = await rig.Auth.LoginAsync(Creds(), "1.1.1.1", ChromeWin, laptop);
        var before = (await rig.Auth.GetSessionsAsync(rig.UserId, null)).Value.Single().SessionId;

        var refreshed = await rig.Auth.RefreshTokenAsync(login.Value.RefreshToken!, "1.1.1.1", laptop);

        refreshed.IsSuccess.Should().BeTrue();
        refreshed.Value.RefreshToken.Should().NotBe(login.Value.RefreshToken);
        (await rig.Auth.GetSessionsAsync(rig.UserId, null)).Value.Single().SessionId.Should().Be(before);
    }

    [Fact]
    public async Task A_refresh_token_does_not_work_without_its_device()
    {
        var rig = await CreateRigAsync();
        var login = await rig.Auth.LoginAsync(Creds(), "1.1.1.1", ChromeWin, Device());

        var stolen = await rig.Auth.RefreshTokenAsync(login.Value.RefreshToken!, "6.6.6.6", Device());

        stolen.IsSuccess.Should().BeFalse();
        // Rejecting is enough: the real device is not signed out by someone else's failed attempt.
        (await rig.Auth.GetSessionsAsync(rig.UserId, null)).Value.Should().HaveCount(1);
    }

    [Fact]
    public async Task Replaying_a_rotated_token_signs_the_whole_session_out()
    {
        var rig = await CreateRigAsync(new SessionSecuritySettings { RefreshReuseGraceSeconds = 0 });
        var laptop = Device();
        var login = await rig.Auth.LoginAsync(Creds(), "1.1.1.1", ChromeWin, laptop);
        var oldToken = login.Value.RefreshToken!;
        var rotated = await rig.Auth.RefreshTokenAsync(oldToken, "1.1.1.1", laptop);
        rotated.IsSuccess.Should().BeTrue();

        await Task.Delay(20); // past the (zero) grace window
        var replay = await rig.Auth.RefreshTokenAsync(oldToken, "6.6.6.6", laptop);

        replay.IsSuccess.Should().BeFalse();
        (await rig.Auth.GetSessionsAsync(rig.UserId, null)).Value.Should().BeEmpty("reuse means the token leaked");
        (await rig.Auth.RefreshTokenAsync(rotated.Value.RefreshToken!, "1.1.1.1", laptop)).IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task Replaying_a_token_inside_the_grace_window_is_treated_as_a_tab_race()
    {
        var rig = await CreateRigAsync(new SessionSecuritySettings { RefreshReuseGraceSeconds = 60 });
        var laptop = Device();
        var login = await rig.Auth.LoginAsync(Creds(), "1.1.1.1", ChromeWin, laptop);
        var rotated = await rig.Auth.RefreshTokenAsync(login.Value.RefreshToken!, "1.1.1.1", laptop);

        var second = await rig.Auth.RefreshTokenAsync(login.Value.RefreshToken!, "1.1.1.1", laptop);

        second.IsSuccess.Should().BeFalse();
        (await rig.Auth.RefreshTokenAsync(rotated.Value.RefreshToken!, "1.1.1.1", laptop)).IsSuccess
            .Should().BeTrue("the newer token must survive a simple race");
    }

    // ── New-device email ──────────────────────────────────────────────────────────

    [Fact]
    public async Task First_device_is_the_baseline_and_sends_no_email()
    {
        var rig = await CreateRigAsync();
        await rig.Auth.LoginAsync(Creds(), "1.1.1.1", ChromeWin, Device());
        rig.Email.Sent.Should().BeEmpty();
    }

    [Fact]
    public async Task A_new_device_triggers_an_email_but_a_known_one_does_not()
    {
        var rig = await CreateRigAsync();
        var laptop = Device();
        await rig.Auth.LoginAsync(Creds(), "1.1.1.1", ChromeWin, laptop);

        await rig.Auth.LoginAsync(Creds(), "1.1.1.1", ChromeWin, laptop);
        rig.Email.Sent.Should().BeEmpty("the laptop is already known");

        await rig.Auth.LoginAsync(Creds(), "8.8.8.8", SafariIos, Device());
        var mail = rig.Email.Sent.Should().ContainSingle().Subject;
        mail.To.Should().Be("tester@example.com");
        mail.Body.Should().Contain("Safari on iOS").And.Contain("8.8.8.8");
    }

    [Fact]
    public async Task Email_can_be_switched_off()
    {
        var rig = await CreateRigAsync(new SessionSecuritySettings { NotifyOnNewDevice = false });
        await rig.Auth.LoginAsync(Creds(), "1.1.1.1", ChromeWin, Device());
        await rig.Auth.LoginAsync(Creds(), "8.8.8.8", SafariIos, Device());
        rig.Email.Sent.Should().BeEmpty();
    }

    [Fact]
    public async Task Wrong_password_creates_no_session()
    {
        var rig = await CreateRigAsync();
        var result = await rig.Auth.LoginAsync(new LoginUserDto { UserName = "tester", Password = "nope" }, "1.1.1.1", ChromeWin, Device());
        result.IsSuccess.Should().BeFalse();
        (await rig.Auth.GetSessionsAsync(rig.UserId, null)).Value.Should().BeEmpty();
    }

    // ── Device label ──────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(ChromeWin, "Chrome on Windows")]
    [InlineData(SafariIos, "Safari on iOS")]
    [InlineData("Mozilla/5.0 (X11; Linux x86_64; rv:121.0) Gecko/20100101 Firefox/121.0", "Firefox on Linux")]
    [InlineData("Mozilla/5.0 (Windows NT 10.0) AppleWebKit/537.36 Chrome/120.0 Safari/537.36 Edg/120.0", "Edge on Windows")]
    [InlineData("", "Unknown device")]
    [InlineData(null, "Unknown device")]
    public void DeviceInfo_describes_the_browser_and_os(string? userAgent, string expected)
        => DeviceInfo.Describe(userAgent).Should().Be(expected);
}
