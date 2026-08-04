using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;
using Microsoft.AspNetCore.Identity;

namespace Infrastructure.Security;

/// <summary>
/// Replaces ASP.NET Core Identity's default password hasher (PBKDF2-HMAC-SHA256 —
/// not BCrypt, worth being precise about that) with Argon2id, OWASP's current
/// recommended algorithm.
///
/// MIGRATION SAFETY — this is the part that actually matters in production:
/// existing users' passwords are already hashed with Identity's legacy format.
/// Swapping the hasher wholesale would lock every existing user out immediately.
/// Instead, this hasher recognizes BOTH formats: new hashes use Argon2id, but
/// verification against an old-format hash still works, and on success returns
/// PasswordVerificationResult.SuccessRehashNeeded — a real enum value ASP.NET
/// Core Identity's SignInManager/UserManager already know how to handle. They
/// automatically call HashPassword again and persist the new Argon2id hash,
/// entirely built-in, no extra plumbing needed here. Users get silently
/// upgraded to Argon2id the next time they log in successfully — no forced
/// password reset, no disruption.
/// </summary>
public class Argon2PasswordHasher<TUser> : IPasswordHasher<TUser> where TUser : class
{
    // Marker byte distinguishing our Argon2id format from Identity's legacy
    // formats (which start with 0x00 or 0x01). Arbitrary but must not collide.
    private const byte Argon2idFormatMarker = 0x61;

    // Argon2id parameters — OWASP baseline as of 2024/2025 guidance.
    private const int SaltSizeBytes = 16;   // 128-bit salt
    private const int HashSizeBytes = 32;   // 256-bit output
    private const int MemorySizeKb = 65536; // 64 MB
    private const int Iterations = 3;
    private const int Parallelism = 4;

    // Used only to verify (never to create) existing legacy-format hashes.
    private readonly PasswordHasher<TUser> _legacyHasher = new();

    public string HashPassword(TUser user, string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSizeBytes);
        var hash = ComputeArgon2idHash(password, salt);

        var combined = new byte[1 + SaltSizeBytes + HashSizeBytes];
        combined[0] = Argon2idFormatMarker;
        Buffer.BlockCopy(salt, 0, combined, 1, SaltSizeBytes);
        Buffer.BlockCopy(hash, 0, combined, 1 + SaltSizeBytes, HashSizeBytes);

        return Convert.ToBase64String(combined);
    }

    public PasswordVerificationResult VerifyHashedPassword(TUser user, string hashedPassword, string providedPassword)
    {
        byte[] decoded;
        try
        {
            decoded = Convert.FromBase64String(hashedPassword);
        }
        catch (FormatException)
        {
            return PasswordVerificationResult.Failed;
        }

        bool isArgon2idFormat = decoded.Length == 1 + SaltSizeBytes + HashSizeBytes
                              && decoded[0] == Argon2idFormatMarker;

        if (isArgon2idFormat)
        {
            var salt = new byte[SaltSizeBytes];
            var expectedHash = new byte[HashSizeBytes];
            Buffer.BlockCopy(decoded, 1, salt, 0, SaltSizeBytes);
            Buffer.BlockCopy(decoded, 1 + SaltSizeBytes, expectedHash, 0, HashSizeBytes);

            var actualHash = ComputeArgon2idHash(providedPassword, salt);

            // Constant-time comparison — avoids leaking hash-match information
            // through response-time differences.
            return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash)
                ? PasswordVerificationResult.Success
                : PasswordVerificationResult.Failed;
        }

        // Not our format — this is an existing user still on Identity's legacy
        // hash. Verify against that, and flag for transparent rehash on success.
        var legacyResult = _legacyHasher.VerifyHashedPassword(user, hashedPassword, providedPassword);

        return legacyResult == PasswordVerificationResult.Success
            ? PasswordVerificationResult.SuccessRehashNeeded
            : legacyResult;
    }

    private static byte[] ComputeArgon2idHash(string password, byte[] salt)
    {
        using var argon2 = new Argon2id(Encoding.UTF8.GetBytes(password))
        {
            Salt = salt,
            DegreeOfParallelism = Parallelism,
            MemorySize = MemorySizeKb,
            Iterations = Iterations
        };
        return argon2.GetBytes(HashSizeBytes);
    }
}
