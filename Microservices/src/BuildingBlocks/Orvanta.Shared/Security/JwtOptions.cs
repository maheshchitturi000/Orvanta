namespace Orvanta.Shared.Security;

/// <summary>
/// JWT settings shared by every service. Keys must come from configuration
/// (user-secrets / environment variables / a vault in production) and never be
/// committed for production use.
/// </summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = "orvanta-auth";
    public string Audience { get; set; } = "orvanta-api";

    /// <summary>Base64 key (>= 32 bytes) used to sign tokens with HMAC-SHA256.</summary>
    public string SigningKey { get; set; } = string.Empty;

    /// <summary>
    /// Base64 key (exactly 32 bytes) used to encrypt tokens (JWE, A256KW + A256CBC-HS512),
    /// so claims are not readable by anyone holding the token.
    /// </summary>
    public string EncryptionKey { get; set; } = string.Empty;

    public int AccessTokenMinutes { get; set; } = 15;
    public int RefreshTokenDays { get; set; } = 7;
}
