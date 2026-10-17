using Microsoft.IdentityModel.Tokens;

namespace Orvanta.Shared.Security;

/// <summary>Builds signing and encryption credentials from <see cref="JwtOptions"/>.</summary>
public static class JwtKeys
{
    public static SymmetricSecurityKey SigningKey(JwtOptions options)
    {
        var bytes = Decode(options.SigningKey, nameof(options.SigningKey));
        if (bytes.Length < 32)
        {
            throw new InvalidOperationException("Jwt:SigningKey must be at least 32 bytes (base64).");
        }

        return new SymmetricSecurityKey(bytes) { KeyId = "orvanta-sig" };
    }

    public static SymmetricSecurityKey EncryptionKey(JwtOptions options)
    {
        var bytes = Decode(options.EncryptionKey, nameof(options.EncryptionKey));
        if (bytes.Length != 32)
        {
            throw new InvalidOperationException("Jwt:EncryptionKey must be exactly 32 bytes (base64).");
        }

        return new SymmetricSecurityKey(bytes) { KeyId = "orvanta-enc" };
    }

    public static SigningCredentials SigningCredentials(JwtOptions options) =>
        new(SigningKey(options), SecurityAlgorithms.HmacSha256);

    public static EncryptingCredentials EncryptingCredentials(JwtOptions options) =>
        new(EncryptionKey(options), SecurityAlgorithms.Aes256KW, SecurityAlgorithms.Aes256CbcHmacSha512);

    private static byte[] Decode(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Jwt:{name} is not configured.");
        }

        try
        {
            return Convert.FromBase64String(value);
        }
        catch (FormatException)
        {
            throw new InvalidOperationException($"Jwt:{name} must be a base64 string.");
        }
    }
}
