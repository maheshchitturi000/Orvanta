namespace Orvanta.AuthService.Domain;

/// <summary>One-time password sent by email to confirm a registration.</summary>
public sealed class EmailOtp
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public User? User { get; set; }

    /// <summary>HMAC-SHA256 of the code. The plain code is only ever sent by email.</summary>
    public string CodeHash { get; set; } = string.Empty;

    public DateTime ExpiresAtUtc { get; set; }
    public int FailedAttempts { get; set; }
    public DateTime? ConsumedAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public bool IsActive(DateTime nowUtc) => ConsumedAtUtc is null && ExpiresAtUtc > nowUtc;
}
