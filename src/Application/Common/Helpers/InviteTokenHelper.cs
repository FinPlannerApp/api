using System.Security.Cryptography;
using System.Text;

namespace Application.Common.Helpers;

public static class InviteTokenHelper
{
    /// <summary>
    /// 24 bytes of cryptographic randomness, base64url-encoded — same
    /// approach as the existing SplitGroup.ShareToken generation, kept
    /// consistent rather than inventing a second convention.
    /// </summary>
    public static string GenerateToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(24);
        return Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }

    /// <summary>
    /// A fast cryptographic hash, not a slow password hash like Argon2id
    /// — deliberately different from how user passwords are hashed
    /// elsewhere in this app. A password is low-entropy and user-chosen,
    /// which is exactly why it needs a slow hash to resist brute-forcing.
    /// This token is already 24 bytes of high-entropy randomness; SHA-256
    /// is the right tool here, not overkill or underkill.
    /// </summary>
    public static string Hash(string token)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes);
    }
}
