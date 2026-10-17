namespace Orvanta.AuthService.Security;

public sealed class AuthSecurityOptions
{
    public const string SectionName = "AuthSecurity";

    /// <summary>Secret appended to passwords before hashing. Store outside the database.</summary>
    public string PasswordPepper { get; set; } = string.Empty;

    /// <summary>Base64 key (32 bytes) used to HMAC one-time passwords.</summary>
    public string OtpHmacKey { get; set; } = string.Empty;

    public int OtpLength { get; set; } = 6;
    public int OtpExpiryMinutes { get; set; } = 10;
    public int OtpMaxAttempts { get; set; } = 5;
    public int OtpResendCooldownSeconds { get; set; } = 60;

    public int MaxFailedLogins { get; set; } = 5;
    public int LockoutMinutes { get; set; } = 15;

    public string TermsVersion { get; set; } = "2026-10";

    public string RefreshCookieName { get; set; } = "orv_rt";

    /// <summary>Send the refresh cookie only over HTTPS. Browsers treat http://localhost as secure.</summary>
    public bool RefreshCookieSecure { get; set; } = true;
}
