using System.Security.Cryptography;
using System.Text;

namespace Orvanta.AuthService.Security;

public interface IPasswordHasher
{
    string Hash(string password);
    PasswordVerification Verify(string password, string storedHash);
}

public enum PasswordVerification { Failed, Success, SuccessRehashNeeded }

/// <summary>
/// PBKDF2-HMAC-SHA256 with a per-user random salt and an application-wide
/// pepper (kept outside the database). 600,000 iterations follows the current
/// OWASP Password Storage Cheat Sheet recommendation.
/// Stored format: <c>pbkdf2-sha256$iterations$base64(salt)$base64(hash)</c>.
/// </summary>
public sealed class PasswordHasher(string pepper) : IPasswordHasher
{
    private const string Scheme = "pbkdf2-sha256";
    private const int Iterations = 600_000;
    private const int SaltSize = 16;
    private const int HashSize = 32;

    public string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Derive(password, salt, Iterations);
        return $"{Scheme}${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public PasswordVerification Verify(string password, string storedHash)
    {
        var parts = storedHash.Split('$');
        if (parts.Length != 4 || parts[0] != Scheme || !int.TryParse(parts[1], out var iterations))
        {
            return PasswordVerification.Failed;
        }

        byte[] salt, expected;
        try
        {
            salt = Convert.FromBase64String(parts[2]);
            expected = Convert.FromBase64String(parts[3]);
        }
        catch (FormatException)
        {
            return PasswordVerification.Failed;
        }

        var actual = Derive(password, salt, iterations);
        if (!CryptographicOperations.FixedTimeEquals(actual, expected))
        {
            return PasswordVerification.Failed;
        }

        return iterations < Iterations ? PasswordVerification.SuccessRehashNeeded : PasswordVerification.Success;
    }

    private byte[] Derive(string password, byte[] salt, int iterations) =>
        Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password + pepper), salt, iterations, HashAlgorithmName.SHA256, HashSize);
}
