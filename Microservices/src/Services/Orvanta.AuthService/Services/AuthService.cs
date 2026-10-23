using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Orvanta.AuthService.Contracts;
using Orvanta.AuthService.Data;
using Orvanta.AuthService.Domain;
using Orvanta.AuthService.Notifications;
using Orvanta.AuthService.Security;
using Orvanta.Shared.Auditing;
using Orvanta.Shared.Responses;
using Orvanta.Shared.Security;

namespace Orvanta.AuthService.Services;

public sealed record ClientInfo(string? IpAddress, string? UserAgent);

public interface IAuthService
{
    Task<ServiceResult<RegisterResult>> RegisterAsync(RegisterRequest request, CancellationToken ct);
    Task<ServiceResult<object>> VerifyOtpAsync(VerifyOtpRequest request, CancellationToken ct);
    Task<ServiceResult<OtpSentResult>> ResendOtpAsync(ResendOtpRequest request, CancellationToken ct);
    Task<ServiceResult<IssuedSession>> LoginAsync(LoginRequest request, ClientInfo client, CancellationToken ct);
    Task<ServiceResult<IssuedSession>> RefreshAsync(string? refreshToken, ClientInfo client, CancellationToken ct);
    Task LogoutAsync(string? refreshToken, CancellationToken ct);
    Task<ServiceResult<UserProfile>> GetProfileAsync(Guid userId, CancellationToken ct);
    Task<ServiceResult<OtpSentResult>> ForgotPasswordAsync(ForgotPasswordRequest request, CancellationToken ct);
    Task<ServiceResult<object>> VerifyResetCodeAsync(VerifyResetCodeRequest request, CancellationToken ct);
    Task<ServiceResult<object>> ResetPasswordAsync(ResetPasswordRequest request, CancellationToken ct);
}

public sealed class AuthService(
    AuthDbContext db,
    IPasswordHasher passwordHasher,
    ITokenService tokenService,
    IOtpService otpService,
    IEmailSender emailSender,
    IOptions<AuthSecurityOptions> securityOptions,
    IOptions<JwtOptions> jwtOptions,
    IAuditLogger audit,
    ILogger<AuthService> logger) : IAuthService
{
    private const string InvalidCredentials = "Incorrect email or password.";
    private static readonly TimeSpan RefreshReuseGrace = TimeSpan.FromSeconds(30);

    // Hash of a random password, used to keep response timing constant for unknown emails.
    private static string? _dummyHash;

    private readonly AuthSecurityOptions _security = securityOptions.Value;
    private readonly JwtOptions _jwt = jwtOptions.Value;

    public async Task<ServiceResult<RegisterResult>> RegisterAsync(RegisterRequest request, CancellationToken ct)
    {
        var email = Normalize(request.Email);
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email, ct);

        if (user is { IsEmailVerified: true })
        {
            return ServiceResult<RegisterResult>.Fail(409, "An account with this email already exists. Please sign in.");
        }

        if (user is null)
        {
            user = new User { Email = email };
            db.Users.Add(user);
        }
        else
        {
            // Unverified account: let the person register again with fresh details, but respect the OTP cooldown.
            var wait = await otpService.GetResendWaitSecondsAsync(user.Id, ct);
            if (wait > 0)
            {
                return ServiceResult<RegisterResult>.Fail(429, $"Please wait {wait} seconds before requesting another code.");
            }
        }

        user.FullName = request.FullName.Trim();
        user.PhoneNumber = request.PhoneNumber.Trim();
        user.PasswordHash = passwordHasher.Hash(request.Password);
        user.TermsVersion = _security.TermsVersion;
        user.TermsAcceptedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Registration started for user {UserId}", user.Id);
        audit.Log("Auth.Register", true, user.Id, user.Email);

        var result = new RegisterResult(user.Id, user.Email, _security.OtpExpiryMinutes * 60, _security.OtpResendCooldownSeconds);

        if (!await TrySendOtpAsync(user, ct))
        {
            // The account exists; the person can use "Resend code" once email delivery works.
            return ServiceResult<RegisterResult>.Fail(502,
                "Your account was created, but we couldn't send the verification email. Please use \"Resend code\" in a moment.", result);
        }

        return ServiceResult<RegisterResult>.Created(result,
            "We've sent a 6-digit code to your email. Enter it to activate your account.");
    }

    public async Task<ServiceResult<object>> VerifyOtpAsync(VerifyOtpRequest request, CancellationToken ct)
    {
        var email = Normalize(request.Email);
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email, ct);

        if (user is null)
        {
            return ServiceResult<object>.Fail(400, "The code is invalid or has expired.");
        }

        if (user.IsEmailVerified)
        {
            return ServiceResult<object>.Ok(new { verified = true }, "Your email is already verified. Please sign in.");
        }

        var check = await otpService.VerifyAsync(user, request.Code.Trim(), OtpPurpose.EmailVerification, consume: true, ct);
        switch (check)
        {
            case OtpCheck.Expired:
                return ServiceResult<object>.Fail(400, "This code has expired. Request a new one.");
            case OtpCheck.TooManyAttempts:
                return ServiceResult<object>.Fail(429, "Too many incorrect attempts. Request a new code.");
            case OtpCheck.Invalid:
                return ServiceResult<object>.Fail(400, "That code is incorrect. Please try again.");
        }

        user.IsEmailVerified = true;
        user.EmailVerifiedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Email verified for user {UserId}", user.Id);
        audit.Log("Auth.EmailVerified", true, user.Id, user.Email);
        return ServiceResult<object>.Ok(new { verified = true }, "Email verified. You can now sign in.");
    }

    public async Task<ServiceResult<OtpSentResult>> ResendOtpAsync(ResendOtpRequest request, CancellationToken ct)
    {
        var email = Normalize(request.Email);
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email, ct);
        var result = new OtpSentResult(email, _security.OtpExpiryMinutes * 60, _security.OtpResendCooldownSeconds);

        // Same response whether or not the account exists, so emails can't be probed.
        if (user is null || user.IsEmailVerified)
        {
            return ServiceResult<OtpSentResult>.Ok(result, "If an unverified account exists, a new code has been sent.");
        }

        var wait = await otpService.GetResendWaitSecondsAsync(user.Id, ct);
        if (wait > 0)
        {
            return ServiceResult<OtpSentResult>.Fail(429, $"Please wait {wait} seconds before requesting another code.");
        }

        return await TrySendOtpAsync(user, ct)
            ? ServiceResult<OtpSentResult>.Ok(result, "A new code has been sent to your email.")
            : ServiceResult<OtpSentResult>.Fail(502, "We couldn't send the email right now. Please try again in a minute.");
    }

    public async Task<ServiceResult<IssuedSession>> LoginAsync(LoginRequest request, ClientInfo client, CancellationToken ct)
    {
        var email = Normalize(request.Email);
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email, ct);
        var now = DateTime.UtcNow;

        if (user is null)
        {
            _dummyHash ??= passwordHasher.Hash(SecureHash.RandomToken(16));
            passwordHasher.Verify(request.Password, _dummyHash);
            audit.Log("Auth.LoginFailed", false, userEmail: email, details: "Unknown email");
            return ServiceResult<IssuedSession>.Fail(401, InvalidCredentials);
        }

        if (user.LockoutEndUtc > now)
        {
            var minutes = (int)Math.Ceiling((user.LockoutEndUtc.Value - now).TotalMinutes);
            audit.Log("Auth.LoginFailed", false, user.Id, user.Email, details: "Account locked");
            return ServiceResult<IssuedSession>.Fail(423, $"Too many failed attempts. Try again in {minutes} minute(s).");
        }

        var verification = passwordHasher.Verify(request.Password, user.PasswordHash);
        if (verification == PasswordVerification.Failed)
        {
            user.FailedLoginCount++;
            if (user.FailedLoginCount >= _security.MaxFailedLogins)
            {
                user.FailedLoginCount = 0;
                user.LockoutEndUtc = now.AddMinutes(_security.LockoutMinutes);
                logger.LogWarning("User {UserId} locked out after repeated failed logins", user.Id);
                audit.Log("Auth.Lockout", false, user.Id, user.Email, details: $"Locked for {_security.LockoutMinutes} minutes");
            }

            await db.SaveChangesAsync(ct);
            audit.Log("Auth.LoginFailed", false, user.Id, user.Email, details: "Wrong password");
            return ServiceResult<IssuedSession>.Fail(401, InvalidCredentials);
        }

        if (!user.IsEmailVerified)
        {
            if (await otpService.GetResendWaitSecondsAsync(user.Id, ct) == 0)
            {
                await TrySendOtpAsync(user, ct);
            }

            return ServiceResult<IssuedSession>.Fail(403, "Please verify your email first. We've sent you a code.");
        }

        if (verification == PasswordVerification.SuccessRehashNeeded)
        {
            user.PasswordHash = passwordHasher.Hash(request.Password);
        }

        user.FailedLoginCount = 0;
        user.LockoutEndUtc = null;
        user.LastLoginAtUtc = now;

        var issued = IssueSession(user, Guid.NewGuid(), client);
        await db.SaveChangesAsync(ct);

        logger.LogInformation("User {UserId} signed in", user.Id);
        audit.Log("Auth.Login", true, user.Id, user.Email);
        return ServiceResult<IssuedSession>.Ok(issued, $"Welcome back, {user.FullName}!");
    }

    public async Task<ServiceResult<IssuedSession>> RefreshAsync(string? refreshToken, ClientInfo client, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            return ServiceResult<IssuedSession>.Fail(401, "Please sign in.");
        }

        var hash = SecureHash.Sha256(refreshToken);
        var stored = await db.RefreshTokens.Include(t => t.User).FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
        var now = DateTime.UtcNow;

        if (stored?.User is null)
        {
            return ServiceResult<IssuedSession>.Fail(401, "Please sign in.");
        }

        // Two clients sharing the cookie (web app + Chrome extension) may refresh at the same moment.
        // A token rotated within the grace window is accepted once more instead of being treated as theft.
        var withinGrace = stored.RevokedAtUtc is not null
                          && stored.ReplacedByTokenHash is not null
                          && now - stored.RevokedAtUtc.Value <= RefreshReuseGrace;

        if (stored.RevokedAtUtc is not null && !withinGrace)
        {
            // A rotated token was presented again: assume theft and end the whole session family.
            await RevokeFamilyAsync(stored.FamilyId, ct);
            logger.LogWarning("Refresh token reuse detected for user {UserId}; session family revoked", stored.UserId);
            audit.Log("Auth.RefreshTokenReuse", false, stored.UserId, stored.User.Email, "Session", stored.FamilyId.ToString());
            return ServiceResult<IssuedSession>.Fail(401, "Your session has ended. Please sign in again.");
        }

        if (stored.ExpiresAtUtc <= now)
        {
            return ServiceResult<IssuedSession>.Fail(401, "Your session has expired. Please sign in again.");
        }

        var issued = IssueSession(stored.User, stored.FamilyId, client);
        if (!withinGrace)
        {
            stored.RevokedAtUtc = now;
            stored.ReplacedByTokenHash = SecureHash.Sha256(issued.RefreshToken);
        }
        await db.SaveChangesAsync(ct);

        audit.Log("Auth.SessionRefreshed", true, stored.UserId, stored.User.Email, "Session", stored.FamilyId.ToString());
        return ServiceResult<IssuedSession>.Ok(issued, "Session refreshed.");
    }

    public async Task LogoutAsync(string? refreshToken, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            return;
        }

        var hash = SecureHash.Sha256(refreshToken);
        var session = await db.RefreshTokens.Where(t => t.TokenHash == hash)
            .Select(t => new { t.FamilyId, t.UserId }).FirstOrDefaultAsync(ct);
        if (session is not null)
        {
            await RevokeFamilyAsync(session.FamilyId, ct);
            audit.Log("Auth.Logout", true, session.UserId, entityType: "Session", entityId: session.FamilyId.ToString());
        }
    }

    public async Task<ServiceResult<UserProfile>> GetProfileAsync(Guid userId, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct);
        return user is null
            ? ServiceResult<UserProfile>.Fail(404, "Account not found.")
            : ServiceResult<UserProfile>.Ok(ToProfile(user));
    }

    public async Task<ServiceResult<OtpSentResult>> ForgotPasswordAsync(ForgotPasswordRequest request, CancellationToken ct)
    {
        var email = Normalize(request.Email);
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email, ct);
        var result = new OtpSentResult(email, _security.OtpExpiryMinutes * 60, _security.OtpResendCooldownSeconds);
        const string message = "If an account exists for this email, we've sent a 6-digit code to reset your password.";

        // Same answer for unknown, unverified and rate-limited accounts, so emails can't be probed.
        if (user is null || !user.IsEmailVerified || await otpService.GetResendWaitSecondsAsync(user.Id, ct) > 0)
        {
            return ServiceResult<OtpSentResult>.Ok(result, message);
        }

        if (!await TrySendOtpAsync(user, ct, OtpPurpose.PasswordReset))
        {
            return ServiceResult<OtpSentResult>.Fail(502, "We couldn't send the email right now. Please try again in a minute.");
        }

        audit.Log("Auth.PasswordResetRequested", true, user.Id, user.Email);
        return ServiceResult<OtpSentResult>.Ok(result, message);
    }

    /// <summary>Checks the code without using it up, so the user can then choose a new password.</summary>
    public async Task<ServiceResult<object>> VerifyResetCodeAsync(VerifyResetCodeRequest request, CancellationToken ct)
    {
        var (user, failure) = await CheckResetCodeAsync(request.Email, request.Code, consume: false, ct);
        return user is null
            ? failure!
            : ServiceResult<object>.Ok(new { verified = true }, "Code verified. Choose a new password.");
    }

    public async Task<ServiceResult<object>> ResetPasswordAsync(ResetPasswordRequest request, CancellationToken ct)
    {
        var (user, failure) = await CheckResetCodeAsync(request.Email, request.Code, consume: false, ct);
        if (user is null)
        {
            return failure!;
        }

        if (passwordHasher.Verify(request.NewPassword, user.PasswordHash) != PasswordVerification.Failed)
        {
            const string sameMessage = "Choose a password different from your current one.";
            return ServiceResult<object>.Fail(400, sameMessage,
                new Dictionary<string, string[]> { ["newPassword"] = [sameMessage] });
        }

        // Use the code up only now, so a rejected password doesn't burn it.
        var (consumed, consumeFailure) = await CheckResetCodeAsync(request.Email, request.Code, consume: true, ct);
        if (consumed is null)
        {
            return consumeFailure!;
        }

        user.PasswordHash = passwordHasher.Hash(request.NewPassword);
        user.FailedLoginCount = 0;
        user.LockoutEndUtc = null;
        await db.SaveChangesAsync(ct);

        // Whoever knew the old password is signed out everywhere.
        var now = DateTime.UtcNow;
        await db.RefreshTokens
            .Where(t => t.UserId == user.Id && t.RevokedAtUtc == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAtUtc, now), ct);

        logger.LogInformation("Password reset for user {UserId}", user.Id);
        audit.Log("Auth.PasswordReset", true, user.Id, user.Email);
        await TrySendPasswordChangedNoticeAsync(user, ct);

        return ServiceResult<object>.Ok(new { reset = true }, "Your password has been updated. Sign in with your new password.");
    }

    /// <summary>Unknown emails get the same answer as wrong codes, so emails can't be probed.</summary>
    private async Task<(User? User, ServiceResult<object>? Failure)> CheckResetCodeAsync(
        string rawEmail, string code, bool consume, CancellationToken ct)
    {
        var email = Normalize(rawEmail);
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email, ct);
        if (user is null || !user.IsEmailVerified)
        {
            return (null, ServiceResult<object>.Fail(400, "That code is incorrect or has expired."));
        }

        var check = await otpService.VerifyAsync(user, code.Trim(), OtpPurpose.PasswordReset, consume, ct);
        if (check != OtpCheck.Valid)
        {
            audit.Log("Auth.PasswordResetCodeFailed", false, user.Id, user.Email, details: check.ToString());
        }

        return check switch
        {
            OtpCheck.Valid => (user, null),
            OtpCheck.Expired => (null, ServiceResult<object>.Fail(400, "This code has expired. Request a new one.")),
            OtpCheck.TooManyAttempts => (null, ServiceResult<object>.Fail(429, "Too many incorrect attempts. Request a new code.")),
            _ => (null, ServiceResult<object>.Fail(400, "That code is incorrect. Please try again."))
        };
    }

    /// <summary>Security notice. Failing to send it must not undo the reset.</summary>
    private async Task TrySendPasswordChangedNoticeAsync(User user, CancellationToken ct)
    {
        try
        {
            await emailSender.SendAsync(
                user.Email,
                "Your Orvanta AI password was changed",
                $"""
                <div style="font-family:Segoe UI,Arial,sans-serif;max-width:480px;margin:auto;padding:24px;border:1px solid #e9e3ff;border-radius:16px">
                  <h2 style="color:#6d4aff;margin-top:0">Password changed</h2>
                  <p>Hi {WebUtility.HtmlEncode(user.FullName)},</p>
                  <p>The password for your Orvanta AI account was just changed, and you've been signed out on all devices.</p>
                  <p style="color:#666">If this wasn't you, reset your password again right away from the sign-in page.</p>
                </div>
                """,
                ct);
        }
        catch (EmailDeliveryException ex)
        {
            logger.LogWarning("Password-changed notice for user {UserId} was not delivered", user.Id);
            audit.Log("Auth.EmailDeliveryFailed", false, user.Id, user.Email, details: $"PasswordChangedNotice: {ex.Message}");
        }
    }

    /// <summary>Issues and emails an OTP. Delivery failures are already logged with the SMTP reason by the sender.</summary>
    private async Task<bool> TrySendOtpAsync(User user, CancellationToken ct, string purpose = OtpPurpose.EmailVerification)
    {
        try
        {
            await otpService.IssueAndSendAsync(user, purpose, ct);
            return true;
        }
        catch (EmailDeliveryException ex)
        {
            logger.LogWarning("{Purpose} email for user {UserId} was not delivered", purpose, user.Id);
            audit.Log("Auth.EmailDeliveryFailed", false, user.Id, user.Email, details: $"{purpose}: {ex.Message}");
            return false;
        }
    }

    private IssuedSession IssueSession(User user, Guid familyId, ClientInfo client)
    {
        var (accessToken, accessExpires) = tokenService.CreateAccessToken(user, familyId);
        var refreshToken = SecureHash.RandomToken();
        var refreshExpires = DateTime.UtcNow.AddMinutes(_jwt.RefreshTokenMinutes);

        db.RefreshTokens.Add(new RefreshToken
        {
            UserId = user.Id,
            FamilyId = familyId,
            TokenHash = SecureHash.Sha256(refreshToken),
            ExpiresAtUtc = refreshExpires,
            CreatedByIp = client.IpAddress,
            UserAgent = client.UserAgent?[..Math.Min(client.UserAgent.Length, 512)]
        });

        var session = new AuthSession(
            accessToken,
            accessExpires,
            (int)(accessExpires - DateTime.UtcNow).TotalSeconds,
            ToProfile(user));

        return new IssuedSession(session, refreshToken, refreshExpires);
    }

    private Task RevokeFamilyAsync(Guid familyId, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        return db.RefreshTokens
            .Where(t => t.FamilyId == familyId && t.RevokedAtUtc == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAtUtc, now), ct);
    }

    private static UserProfile ToProfile(User user) =>
        new(user.Id, user.FullName, user.Email, user.PhoneNumber, user.Role);

    private static string Normalize(string email) => email.Trim().ToLowerInvariant();
}
