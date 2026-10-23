using System.Text.RegularExpressions;

namespace Orvanta.FoodService.Ondc.Crypto;

/// <summary>
/// The ONDC "Authorization: Signature ..." header. The signed text is
/// "(created): {created}\n(expires): {expires}\ndigest: BLAKE-512={base64 BLAKE2b-512 of the exact body}".
/// </summary>
public sealed partial record OndcAuthHeader(string SubscriberId, string UniqueKeyId, long Created, long Expires, string Signature)
{
    public static string Create(string subscriberId, string uniqueKeyId, OndcSigningKey key, byte[] body, TimeSpan validity, DateTimeOffset? now = null)
    {
        var created = (now ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds();
        var expires = created + (long)validity.TotalSeconds;
        var signature = OndcCrypto.Sign(key, SigningString(created, expires, body));

        return $"Signature keyId=\"{subscriberId}|{uniqueKeyId}|ed25519\",algorithm=\"ed25519\",created=\"{created}\"," +
               $"expires=\"{expires}\",headers=\"(created) (expires) digest\",signature=\"{signature}\"";
    }

    public static string SigningString(long created, long expires, byte[] body) =>
        $"(created): {created}\n(expires): {expires}\ndigest: BLAKE-512={OndcCrypto.Blake512Base64(body)}";

    public static OndcAuthHeader? Parse(string? header)
    {
        if (string.IsNullOrWhiteSpace(header))
        {
            return null;
        }

        var fields = FieldPattern().Matches(header).ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value, StringComparer.OrdinalIgnoreCase);
        if (!fields.TryGetValue("keyId", out var keyId) || !fields.TryGetValue("signature", out var signature)
            || !long.TryParse(fields.GetValueOrDefault("created"), out var created)
            || !long.TryParse(fields.GetValueOrDefault("expires"), out var expires))
        {
            return null;
        }

        var parts = keyId.Split('|');
        return parts.Length >= 2 ? new OndcAuthHeader(parts[0], parts[1], created, expires, signature) : null;
    }

    /// <summary>Checks the time window and the signature over this exact body.</summary>
    public bool IsValid(string base64PublicKey, byte[] body, DateTimeOffset? now = null)
    {
        var nowSeconds = (now ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds();
        const long clockSkew = 30;
        if (nowSeconds + clockSkew < Created || nowSeconds - clockSkew > Expires)
        {
            return false;
        }

        return OndcCrypto.Verify(base64PublicKey, SigningString(Created, Expires, body), Signature);
    }

    [GeneratedRegex("(\\w+)=\"([^\"]*)\"")]
    private static partial Regex FieldPattern();
}
