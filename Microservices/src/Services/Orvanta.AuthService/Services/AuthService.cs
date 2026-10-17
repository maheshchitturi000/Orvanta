using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Orvanta.AuthService.Contracts;
using Orvanta.AuthService.Data;
using Orvanta.AuthService.Domain;
using Orvanta.AuthService.Notifications;
using Orvanta.AuthService.Security;
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
}

public sealed class AuthService(
    AuthDbContext db,
    IPasswordHasher passwordHasher,
    ITokenService tokenService,
    IOtpService otpService,
    IOptions<AuthSecurityOptions> securityOptions,
    IOptions<JwtOptions> jwtOptions,
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

        var check = await otpService.VerifyAsync(user, request.Code.Trim(), ct);
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
            return ServiceResult<IssuedSession>.Fail(401, InvalidCredentials);
        }

        if (user.LockoutEndUtc > now)
        {
            var minutes = (int)Math.Ceiling((user.LockoutEndUtc.Value - now).TotalMinutes);
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
            }

            await db.SaveChangesAsync(ct);
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

        return ServiceResult<IssuedSession>.Ok(issued, "Session refreshed.");
    }

    public async Task LogoutAsync(string? refreshToken, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            return;
        }

        var hash = SecureHash.Sha256(refreshToken);
        var familyId = await db.RefreshTokens.Where(t => t.TokenHash == hash).Select(t => (Guid?)t.FamilyId).FirstOrDefaultAsync(ct);
        if (familyId is not null)
        {
            await RevokeFamilyAsync(familyId.Value, ct);
        }
    }

    public async Task<ServiceResult<UserProfile>> GetProfileAsync(Guid userId, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct);
        return user is null
            ? ServiceResult<UserProfile>.Fail(404, "Account not found.")
            : ServiceResult<UserProfile>.Ok(ToProfile(user));
    }

    /// <summary>Issues and emails an OTP. Delivery failures are already logged with the SMTP reason by the sender.</summary>
    private async Task<bool> TrySendOtpAsync(User user, CancellationToken ct)
    {
        try
        {
            await otpService.IssueAndSendAsync(user, ct);
            return true;
        }
        catch (EmailDeliveryException)
        {
            logger.LogWarning("Verification email for user {UserId} was not delivered", user.Id);
            return false;
        }
    }

    private IssuedSession IssueSession(User user, Guid familyId, ClientInfo client)
    {
        var (accessToken, accessExpires) = tokenService.CreateAccessToken(user, familyId);
        var refreshToken = SecureHash.RandomToken();
        var refreshExpires = DateTime.UtcNow.AddDays(_jwt.RefreshTokenDays);

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
