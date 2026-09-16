using Microsoft.AspNetCore.Identity;
using FluentAssertions;
using Infrastructure.Security;
using Xunit;

namespace Application.Tests.Security;

/// <summary>
/// A minimal stand-in user type — the hasher is generic over TUser but
/// never actually reads anything off it, so any reference type works.
/// </summary>
public class TestUser { }

public class Argon2PasswordHasherTests
{
    private readonly Argon2PasswordHasher<TestUser> _hasher = new();
    private readonly TestUser _user = new();

    [Fact]
    public void HashPassword_ThenVerifyWithCorrectPassword_Succeeds()
    {
        var hash = _hasher.HashPassword(_user, "CorrectHorseBatteryStaple1!");

        var result = _hasher.VerifyHashedPassword(_user, hash, "CorrectHorseBatteryStaple1!");

        result.Should().Be(PasswordVerificationResult.Success);
    }

    [Fact]
    public void VerifyHashedPassword_WithWrongPassword_Fails()
    {
        var hash = _hasher.HashPassword(_user, "CorrectHorseBatteryStaple1!");

        var result = _hasher.VerifyHashedPassword(_user, hash, "SomethingElseEntirely");

        result.Should().Be(PasswordVerificationResult.Failed);
    }

    [Fact]
    public void HashPassword_CalledTwiceWithSamePassword_ProducesDifferentHashes()
    {
        // A real, important security property — a random salt per hash
        // means two hashes of the same password should never be
        // identical to each other, which prevents identical passwords
        // from being visually identifiable by comparing stored hashes.
        var hash1 = _hasher.HashPassword(_user, "SamePassword123!");
        var hash2 = _hasher.HashPassword(_user, "SamePassword123!");

        hash1.Should().NotBe(hash2);

        // Both must still independently verify correctly, confirming
        // the difference is purely the salt, not a correctness bug.
        _hasher.VerifyHashedPassword(_user, hash1, "SamePassword123!").Should().Be(PasswordVerificationResult.Success);
        _hasher.VerifyHashedPassword(_user, hash2, "SamePassword123!").Should().Be(PasswordVerificationResult.Success);
    }

    [Fact]
    public void VerifyHashedPassword_WithMalformedBase64_FailsGracefully_DoesNotThrow()
    {
        // A corrupted or tampered stored hash should never crash the
        // login flow — it should simply fail verification.
        var act = () => _hasher.VerifyHashedPassword(_user, "not-valid-base64!!!", "anything");

        act.Should().NotThrow();
        act().Should().Be(PasswordVerificationResult.Failed);
    }

    [Fact]
    public void VerifyHashedPassword_AgainstLegacyIdentityHash_SucceedsAndFlagsForRehash()
    {
        // Simulates an existing user created before this hasher existed
        // — their password is still in ASP.NET Identity's own legacy
        // format. The migration-safety design this class exists for
        // specifically: verification must still succeed against the
        // old format, and must signal that a rehash is needed, rather
        // than either rejecting a genuinely correct password or
        // silently leaving the user on the weaker legacy hash forever.
        var legacyHasher = new PasswordHasher<TestUser>();
        var legacyHash = legacyHasher.HashPassword(_user, "MyOldPassword1!");

        var result = _hasher.VerifyHashedPassword(_user, legacyHash, "MyOldPassword1!");

        result.Should().Be(PasswordVerificationResult.SuccessRehashNeeded);
    }

    [Fact]
    public void VerifyHashedPassword_AgainstLegacyHash_WithWrongPassword_Fails()
    {
        var legacyHasher = new PasswordHasher<TestUser>();
        var legacyHash = legacyHasher.HashPassword(_user, "MyOldPassword1!");

        var result = _hasher.VerifyHashedPassword(_user, legacyHash, "WrongGuess");

        result.Should().Be(PasswordVerificationResult.Failed);
    }

    [Fact]
    public void RehashedPassword_NoLongerReportsRehashNeeded()
    {
        // Confirms the migration actually completes — once a legacy
        // password is rehashed through THIS hasher (simulating what
        // SignInManager does automatically on a successful legacy
        // verification), a subsequent login should report plain
        // Success, not SuccessRehashNeeded a second time.
        var newHash = _hasher.HashPassword(_user, "MyOldPassword1!");

        var result = _hasher.VerifyHashedPassword(_user, newHash, "MyOldPassword1!");

        result.Should().Be(PasswordVerificationResult.Success);
    }
}
