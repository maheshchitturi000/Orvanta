using System.Security.Cryptography;
using System.Text;
using Org.BouncyCastle.Crypto.Agreement;
using Org.BouncyCastle.Crypto.Digests;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.Security;

namespace Orvanta.FoodService.Ondc.Crypto;

/// <summary>Ed25519 key pair; raw keys, Base64 in configuration.</summary>
public sealed record OndcSigningKey(byte[] PrivateSeed, byte[] PublicKey)
{
    public string PublicKeyBase64 => Convert.ToBase64String(PublicKey);
}

/// <summary>Freshly generated keys, in the formats ONDC registration expects.</summary>
public sealed record OndcGeneratedKeys(
    string SigningPrivateKey, string SigningPublicKey, string EncryptionPrivateKey, string EncryptionPublicKey);

/// <summary>
/// The cryptography ONDC prescribes: BLAKE2b-512 body digests, Ed25519 signatures, and an X25519 + AES-256-ECB
/// challenge during registration (on_subscribe).
/// </summary>
public static class OndcCrypto
{
    public static string Blake512Base64(byte[] body)
    {
        var digest = new Blake2bDigest(512);
        digest.BlockUpdate(body, 0, body.Length);
        var hash = new byte[64];
        digest.DoFinal(hash, 0);
        return Convert.ToBase64String(hash);
    }

    /// <summary>Accepts the 32-byte seed or the 64-byte libsodium secret key (seed + public key).</summary>
    public static OndcSigningKey LoadSigningKey(string base64PrivateKey)
    {
        var bytes = Convert.FromBase64String(base64PrivateKey.Trim());
        if (bytes.Length is not (32 or 64))
        {
            throw new InvalidOperationException("Ondc:SigningPrivateKey must be a base64 Ed25519 key (32 or 64 bytes).");
        }

        var seed = bytes[..32];
        var publicKey = new Ed25519PrivateKeyParameters(seed, 0).GeneratePublicKey().GetEncoded();
        return new OndcSigningKey(seed, publicKey);
    }

    public static string Sign(OndcSigningKey key, string text)
    {
        var signer = new Ed25519Signer();
        signer.Init(true, new Ed25519PrivateKeyParameters(key.PrivateSeed, 0));
        var data = Encoding.UTF8.GetBytes(text);
        signer.BlockUpdate(data, 0, data.Length);
        return Convert.ToBase64String(signer.GenerateSignature());
    }

    public static bool Verify(string base64PublicKey, string text, string base64Signature)
    {
        try
        {
            var verifier = new Ed25519Signer();
            verifier.Init(false, new Ed25519PublicKeyParameters(Convert.FromBase64String(base64PublicKey), 0));
            var data = Encoding.UTF8.GetBytes(text);
            verifier.BlockUpdate(data, 0, data.Length);
            return verifier.VerifySignature(Convert.FromBase64String(base64Signature));
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException)
        {
            return false;
        }
    }

    public static OndcSigningKey GenerateSigningKey()
    {
        var generator = new Ed25519KeyPairGenerator();
        generator.Init(new Ed25519KeyGenerationParameters(new SecureRandom()));
        var pair = generator.GenerateKeyPair();
        return new OndcSigningKey(
            ((Ed25519PrivateKeyParameters)pair.Private).GetEncoded(),
            ((Ed25519PublicKeyParameters)pair.Public).GetEncoded());
    }

    /// <summary>A new signing + encryption key pair for registering with ONDC (encryption keys DER-encoded, as ONDC expects).</summary>
    public static OndcGeneratedKeys GenerateRegistrationKeys()
    {
        var signing = GenerateSigningKey();

        var generator = new X25519KeyPairGenerator();
        generator.Init(new X25519KeyGenerationParameters(new SecureRandom()));
        var encryption = generator.GenerateKeyPair();

        return new OndcGeneratedKeys(
            Convert.ToBase64String(signing.PrivateSeed),
            signing.PublicKeyBase64,
            Convert.ToBase64String(Org.BouncyCastle.Pkcs.PrivateKeyInfoFactory.CreatePrivateKeyInfo(encryption.Private).GetDerEncoded()),
            Convert.ToBase64String(Org.BouncyCastle.X509.SubjectPublicKeyInfoFactory.CreateSubjectPublicKeyInfo(encryption.Public).GetDerEncoded()));
    }

    /// <summary>
    /// Answers ONDC's on_subscribe challenge: the shared X25519 secret between Orvanta's encryption key and
    /// ONDC's public key is the AES-256 key; the challenge is AES-ECB encrypted.
    /// </summary>
    public static string DecryptChallenge(string encryptionPrivateKey, string ondcEncryptionPublicKey, string challenge)
    {
        var privateKey = LoadX25519Private(encryptionPrivateKey);
        var publicKey = LoadX25519Public(ondcEncryptionPublicKey);

        var agreement = new X25519Agreement();
        agreement.Init(privateKey);
        var shared = new byte[agreement.AgreementSize];
        agreement.CalculateAgreement(publicKey, shared, 0);

        using var aes = Aes.Create();
        aes.Key = shared;
        var plain = aes.DecryptEcb(Convert.FromBase64String(challenge), PaddingMode.PKCS7);
        return Encoding.UTF8.GetString(plain);
    }

    private static X25519PrivateKeyParameters LoadX25519Private(string base64)
    {
        var bytes = Convert.FromBase64String(base64.Trim());
        return bytes.Length == 32
            ? new X25519PrivateKeyParameters(bytes, 0)
            : (X25519PrivateKeyParameters)PrivateKeyFactory.CreateKey(bytes);
    }

    private static X25519PublicKeyParameters LoadX25519Public(string base64)
    {
        var bytes = Convert.FromBase64String(base64.Trim());
        return bytes.Length == 32
            ? new X25519PublicKeyParameters(bytes, 0)
            : (X25519PublicKeyParameters)PublicKeyFactory.CreateKey(bytes);
    }
}
