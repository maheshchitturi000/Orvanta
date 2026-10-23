using System.ComponentModel.DataAnnotations;

namespace Orvanta.AuthService.Contracts;

public sealed class RegisterRequest
{
    [Required(ErrorMessage = "Full name is required.")]
    [StringLength(100, MinimumLength = 2, ErrorMessage = "Full name must be 2-100 characters.")]
    [RegularExpression(@"^[\p{L} .'-]+$", ErrorMessage = "Full name can contain letters, spaces, . ' and - only.")]
    public string FullName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Email is required.")]
    [EmailAddress(ErrorMessage = "Enter a valid email address.")]
    [StringLength(254)]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Mobile number is required.")]
    [RegularExpression(@"^[6-9]\d{9}$", ErrorMessage = "Enter a valid 10-digit Indian mobile number.")]
    public string PhoneNumber { get; set; } = string.Empty;

    [Required(ErrorMessage = "Password is required.")]
    [StringLength(128, MinimumLength = 8, ErrorMessage = "Password must be 8-128 characters.")]
    [RegularExpression(@"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[^A-Za-z\d]).+$",
        ErrorMessage = "Password needs an uppercase letter, a lowercase letter, a number and a symbol.")]
    public string Password { get; set; } = string.Empty;

    [Required(ErrorMessage = "Please confirm your password.")]
    [Compare(nameof(Password), ErrorMessage = "Passwords do not match.")]
    public string ConfirmPassword { get; set; } = string.Empty;

    [Range(typeof(bool), "true", "true", ErrorMessage = "You must accept the Terms & Conditions.")]
    public bool AcceptTerms { get; set; }
}

public sealed class VerifyOtpRequest
{
    [Required, EmailAddress] public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Enter the code sent to your email.")]
    [RegularExpression(@"^\d{6}$", ErrorMessage = "The code is 6 digits.")]
    public string Code { get; set; } = string.Empty;
}

public sealed class ResendOtpRequest
{
    [Required, EmailAddress] public string Email { get; set; } = string.Empty;
}

public sealed class LoginRequest
{
    [Required(ErrorMessage = "Email is required.")]
    [EmailAddress(ErrorMessage = "Enter a valid email address.")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Password is required.")]
    [StringLength(128)]
    public string Password { get; set; } = string.Empty;
}

public sealed class ForgotPasswordRequest
{
    [Required(ErrorMessage = "Email is required.")]
    [EmailAddress(ErrorMessage = "Enter a valid email address.")]
    [StringLength(254)]
    public string Email { get; set; } = string.Empty;
}

public sealed class VerifyResetCodeRequest
{
    [Required, EmailAddress] public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Enter the code sent to your email.")]
    [RegularExpression(@"^\d{6}$", ErrorMessage = "The code is 6 digits.")]
    public string Code { get; set; } = string.Empty;
}

public sealed class ResetPasswordRequest
{
    [Required, EmailAddress] public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Enter the code sent to your email.")]
    [RegularExpression(@"^\d{6}$", ErrorMessage = "The code is 6 digits.")]
    public string Code { get; set; } = string.Empty;

    [Required(ErrorMessage = "Password is required.")]
    [StringLength(128, MinimumLength = 8, ErrorMessage = "Password must be 8-128 characters.")]
    [RegularExpression(@"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[^A-Za-z\d]).+$",
        ErrorMessage = "Password needs an uppercase letter, a lowercase letter, a number and a symbol.")]
    public string NewPassword { get; set; } = string.Empty;

    [Required(ErrorMessage = "Please confirm your password.")]
    [Compare(nameof(NewPassword), ErrorMessage = "Passwords do not match.")]
    public string ConfirmPassword { get; set; } = string.Empty;
}

public sealed record RegisterResult(Guid UserId, string Email, int OtpExpiresInSeconds, int ResendAfterSeconds);

public sealed record OtpSentResult(string Email, int OtpExpiresInSeconds, int ResendAfterSeconds);

public sealed record VerificationRequiredResult(bool RequiresVerification, string Email);

public sealed record UserProfile(Guid Id, string FullName, string Email, string PhoneNumber, string Role);

/// <summary>Returned on login/refresh. The refresh token travels only in an HttpOnly cookie.</summary>
public sealed record AuthSession(string AccessToken, DateTime ExpiresAtUtc, int ExpiresInSeconds, UserProfile User);

/// <summary>Internal: session plus the raw refresh token to put into the cookie.</summary>
public sealed record IssuedSession(AuthSession Session, string RefreshToken, DateTime RefreshExpiresAtUtc);
