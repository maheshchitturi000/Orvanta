using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Orvanta.Jobs.Core.Data;
using Orvanta.Jobs.Core.Domain;

namespace Orvanta.Jobs.Core.Sources;

/// <summary>
/// Credentials for search sources: the user's own connection (encrypted with AES-256-GCM),
/// falling back to a server-wide key from configuration.
/// </summary>
public sealed class JobSourceCredentials(JobsDbContext db, IOptions<JobsOptions> options)
{
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private readonly JobsOptions _options = options.Value;

    public async Task<string?> GetKeyAsync(Guid userId, string source, CancellationToken ct)
    {
        var connection = await db.SourceConnections.AsNoTracking()
            .FirstOrDefaultAsync(c => c.UserId == userId && c.Source == source, ct);
        if (connection is not null && TryDecrypt(connection.SecretEncrypted) is { } key)
        {
            return key;
        }

        return source == "jsearch" && !string.IsNullOrWhiteSpace(_options.JSearch.ApiKey) && _options.JSearch.Enabled
            ? _options.JSearch.ApiKey
            : null;
    }

    public async Task<(bool Connected, bool ServerKey, DateTime? ConnectedAtUtc)> StatusAsync(Guid userId, string source, CancellationToken ct)
    {
        var connection = await db.SourceConnections.AsNoTracking()
            .FirstOrDefaultAsync(c => c.UserId == userId && c.Source == source, ct);
        var serverKey = source == "jsearch" && _options.JSearch.IsConfigured;
        return (connection is not null || serverKey, connection is null && serverKey, connection?.ConnectedAtUtc);
    }

    public async Task SaveAsync(Guid userId, string source, string secret, CancellationToken ct)
    {
        var connection = await db.SourceConnections.FirstOrDefaultAsync(c => c.UserId == userId && c.Source == source, ct);
        if (connection is null)
        {
            connection = new JobSourceConnection { UserId = userId, Source = source };
            db.SourceConnections.Add(connection);
        }

        connection.SecretEncrypted = Encrypt(secret);
        connection.ConnectedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    public Task DeleteAsync(Guid userId, string source, CancellationToken ct) =>
        db.SourceConnections.Where(c => c.UserId == userId && c.Source == source).ExecuteDeleteAsync(ct);

    private byte[] Key()
    {
        var key = string.IsNullOrWhiteSpace(_options.SecretsKey) ? [] : Convert.FromBase64String(_options.SecretsKey);
        return key.Length == 32
            ? key
            : throw new InvalidOperationException("Jobs:SecretsKey must be a base64 32-byte key (run Microservices/setup-secrets.ps1).");
    }

    private string Encrypt(string plaintext)
    {
        var plain = Encoding.UTF8.GetBytes(plaintext);
        var buffer = new byte[NonceSize + TagSize + plain.Length];
        RandomNumberGenerator.Fill(buffer.AsSpan(0, NonceSize));
        using var aes = new AesGcm(Key(), TagSize);
        aes.Encrypt(buffer.AsSpan(0, NonceSize), plain, buffer.AsSpan(NonceSize + TagSize), buffer.AsSpan(NonceSize, TagSize));
        return Convert.ToBase64String(buffer);
    }

    private string? TryDecrypt(string value)
    {
        try
        {
            var buffer = Convert.FromBase64String(value);
            var plain = new byte[buffer.Length - NonceSize - TagSize];
            using var aes = new AesGcm(Key(), TagSize);
            aes.Decrypt(buffer.AsSpan(0, NonceSize), buffer.AsSpan(NonceSize + TagSize), buffer.AsSpan(NonceSize, TagSize), plain);
            return Encoding.UTF8.GetString(plain);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException or ArgumentException or InvalidOperationException)
        {
            return null; // key rotated or misconfigured: treat as not connected
        }
    }
}
