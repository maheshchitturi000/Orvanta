using System.Net;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Orvanta.AuthService.Data;
using Orvanta.AuthService.Domain;
using Orvanta.AuthService.Notifications;
using Orvanta.AuthService.Security;
using Orvanta.Shared.Security;

namespace Orvanta.AuthService.Services;

public enum OtpCheck { Valid, Invalid, Expired, TooManyAttempts }

public interface IOtpService
{
    /// <summary>Seconds the caller must still wait before another OTP can be sent (0 = can send now).</summary>
    Task<int> GetResendWaitSecondsAsync(Guid userId, CancellationToken ct);

    Task IssueAndSendAsync(User user, CancellationToken ct);

    Task<OtpCheck> VerifyAsync(User user, string code, CancellationToken ct);
}

public sealed class OtpService(
    AuthDbContext db,
    IEmailSender emailSender,
    IOptions<AuthSecurityOptions> options) : IOtpService
{
    private readonly AuthSecurityOptions _options = options.Value;

    public async Task<int> GetResendWaitSecondsAsync(Guid userId, CancellationToken ct)
    {
        var lastSentAt = await db.EmailOtps
            .Where(o => o.UserId == userId)
            .OrderByDescending(o => o.CreatedAtUtc)
            .Select(o => (DateTime?)o.CreatedAtUtc)
            .FirstOrDefaultAsync(ct);

        if (lastSentAt is null)
        {
            return 0;
        }

        var wait = _options.OtpResendCooldownSeconds - (DateTime.UtcNow - lastSentAt.Value).TotalSeconds;
        return wait > 0 ? (int)Math.Ceiling(wait) : 0;
    }

    public async Task IssueAndSendAsync(User user, CancellationToken ct)
    {
        var now = DateTime.UtcNow;

        // Only the newest code is valid.
        await db.EmailOtps
            .Where(o => o.UserId == user.Id && o.ConsumedAtUtc == null)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.ConsumedAtUtc, now), ct);

        var code = GenerateCode(_options.OtpLength);
        db.EmailOtps.Add(new EmailOtp
        {
            UserId = user.Id,
            CodeHash = SecureHash.HmacSha256(code, _options.OtpHmacKey),
            ExpiresAtUtc = now.AddMinutes(_options.OtpExpiryMinutes),
            CreatedAtUtc = now
        });
        await db.SaveChangesAsync(ct);

        await emailSender.SendAsync(
            user.Email,
            $"{code} is your Orvanta AI verification code",
            $"""
            <div style="font-family:Segoe UI,Arial,sans-serif;max-width:480px;margin:auto;padding:24px;border:1px solid #e9e3ff;border-radius:16px">
              <h2 style="color:#6d4aff;margin-top:0">Verify your email</h2>
              <p>Hi {WebUtility.HtmlEncode(user.FullName)},</p>
              <p>Use this code to finish creating your Orvanta AI account:</p>
              <p style="font-size:32px;letter-spacing:8px;font-weight:700;color:#2e1a6b">{code}</p>
              <p style="color:#666">The code expires in {_options.OtpExpiryMinutes} minutes. If you didn't request it, you can ignore this email.</p>
            </div>
            """,
            ct);
    }

    public async Task<OtpCheck> VerifyAsync(User user, string code, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var otp = await db.EmailOtps
            .Where(o => o.UserId == user.Id && o.ConsumedAtUtc == null)
            .OrderByDescending(o => o.CreatedAtUtc)
            .FirstOrDefaultAsync(ct);

        if (otp is null || !otp.IsActive(now))
        {
            return OtpCheck.Expired;
        }

        if (otp.FailedAttempts >= _options.OtpMaxAttempts)
        {
            return OtpCheck.TooManyAttempts;
        }

        var hash = SecureHash.HmacSha256(code, _options.OtpHmacKey);
        if (!SecureHash.FixedTimeEquals(hash, otp.CodeHash))
        {
            otp.FailedAttempts++;
            await db.SaveChangesAsync(ct);
            return otp.FailedAttempts >= _options.OtpMaxAttempts ? OtpCheck.TooManyAttempts : OtpCheck.Invalid;
        }

        otp.ConsumedAtUtc = now;
        await db.SaveChangesAsync(ct);
        return OtpCheck.Valid;
    }

    private static string GenerateCode(int length)
    {
        var max = (int)Math.Pow(10, length);
        return RandomNumberGenerator.GetInt32(0, max).ToString(new string('0', length));
    }
}
