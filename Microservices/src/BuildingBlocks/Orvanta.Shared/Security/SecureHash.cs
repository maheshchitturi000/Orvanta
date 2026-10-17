using System.Security.Cryptography;
using System.Text;

namespace Orvanta.Shared.Security;

/// <summary>Helpers for high-entropy secrets (refresh tokens, OTPs, OAuth state).</summary>
public static class SecureHash
{
    /// <summary>Random URL-safe token with the given number of bytes of entropy.</summary>
    public static string RandomToken(int bytes = 64) =>
        Base64UrlEncode(RandomNumberGenerator.GetBytes(bytes));

    /// <summary>SHA-256 hex digest. Fine for high-entropy values; never use for passwords.</summary>
    public static string Sha256(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    /// <summary>Keyed hash (HMAC-SHA256) for low-entropy values such as OTPs.</summary>
    public static string HmacSha256(string value, string base64Key) =>
        Convert.ToHexString(HMACSHA256.HashData(Convert.FromBase64String(base64Key), Encoding.UTF8.GetBytes(value)));

    public static bool FixedTimeEquals(string a, string b) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));

    public static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
