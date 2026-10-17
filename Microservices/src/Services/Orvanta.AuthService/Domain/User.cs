namespace Orvanta.AuthService.Domain;

public sealed class User
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string FullName { get; set; } = string.Empty;

    /// <summary>Lower-cased, trimmed email. Unique.</summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>10-digit Indian mobile number (needed by food delivery partners).</summary>
    public string PhoneNumber { get; set; } = string.Empty;

    /// <summary>PBKDF2 hash, never the password itself. See <c>PasswordHasher</c>.</summary>
    public string PasswordHash { get; set; } = string.Empty;

    public string Role { get; set; } = Orvanta.Shared.Security.OrvantaRoles.User;

    public bool IsEmailVerified { get; set; }
    public DateTime? EmailVerifiedAtUtc { get; set; }

    public string TermsVersion { get; set; } = string.Empty;
    public DateTime TermsAcceptedAtUtc { get; set; }

    public int FailedLoginCount { get; set; }
    public DateTime? LockoutEndUtc { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? LastLoginAtUtc { get; set; }

    public List<RefreshToken> RefreshTokens { get; set; } = [];
    public List<EmailOtp> EmailOtps { get; set; } = [];
}
