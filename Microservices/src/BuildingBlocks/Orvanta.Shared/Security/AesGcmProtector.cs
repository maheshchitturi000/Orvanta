using System.Security.Cryptography;
using System.Text;

namespace Orvanta.Shared.Security;

/// <summary>
/// Authenticated encryption (AES-256-GCM) for secrets stored at rest, such as
/// third-party OAuth tokens. Output format: base64(nonce | tag | ciphertext).
/// </summary>
public sealed class AesGcmProtector
{
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private readonly byte[] _key;

    public AesGcmProtector(string base64Key)
    {
        _key = Convert.FromBase64String(base64Key);
        if (_key.Length != 32)
        {
            throw new InvalidOperationException("Data protection key must be exactly 32 bytes (base64).");
        }
    }

    public string Protect(string plaintext)
    {
        var plainBytes = Encoding.UTF8.GetBytes(plaintext);
        var buffer = new byte[NonceSize + TagSize + plainBytes.Length];
        var nonce = buffer.AsSpan(0, NonceSize);
        var tag = buffer.AsSpan(NonceSize, TagSize);
        var cipher = buffer.AsSpan(NonceSize + TagSize);

        RandomNumberGenerator.Fill(nonce);
        using var aes = new AesGcm(_key, TagSize);
        aes.Encrypt(nonce, plainBytes, cipher, tag);

        return Convert.ToBase64String(buffer);
    }

    public string Unprotect(string protectedValue)
    {
        var buffer = Convert.FromBase64String(protectedValue);
        if (buffer.Length < NonceSize + TagSize)
        {
            throw new CryptographicException("Protected value is malformed.");
        }

        var nonce = buffer.AsSpan(0, NonceSize);
        var tag = buffer.AsSpan(NonceSize, TagSize);
        var cipher = buffer.AsSpan(NonceSize + TagSize);
        var plain = new byte[cipher.Length];

        using var aes = new AesGcm(_key, TagSize);
        aes.Decrypt(nonce, cipher, tag, plain);

        return Encoding.UTF8.GetString(plain);
    }
}
