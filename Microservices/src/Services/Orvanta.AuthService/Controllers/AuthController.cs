using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Orvanta.AuthService.Contracts;
using Orvanta.AuthService.Security;
using Orvanta.AuthService.Services;
using Orvanta.Shared.Auditing;
using Orvanta.Shared.Responses;
using Orvanta.Shared.Security;

namespace Orvanta.AuthService.Controllers;

[Route("api/auth")]
[SkipAudit] // AuthService writes its own audit events (sign-in, reset, sign-out...) with the user they concern.
public sealed class AuthController(IAuthService authService, IOptions<AuthSecurityOptions> securityOptions) : ApiControllerBase
{
    private const string RefreshCookiePath = "/api/auth";
    private readonly AuthSecurityOptions _security = securityOptions.Value;

    [HttpPost("register")]
    [AllowAnonymous]
    public async Task<ActionResult<ResponseCls<RegisterResult>>> Register(RegisterRequest request, CancellationToken ct) =>
        Respond(await authService.RegisterAsync(request, ct));

    [HttpPost("verify-otp")]
    [AllowAnonymous]
    public async Task<ActionResult<ResponseCls<object>>> VerifyOtp(VerifyOtpRequest request, CancellationToken ct) =>
        Respond(await authService.VerifyOtpAsync(request, ct));

    [HttpPost("resend-otp")]
    [AllowAnonymous]
    public async Task<ActionResult<ResponseCls<OtpSentResult>>> ResendOtp(ResendOtpRequest request, CancellationToken ct) =>
        Respond(await authService.ResendOtpAsync(request, ct));

    /// <summary>Step 1 of "forgot password": emails a reset code. Same answer whether or not the account exists.</summary>
    [HttpPost("forgot-password")]
    [AllowAnonymous]
    public async Task<ActionResult<ResponseCls<OtpSentResult>>> ForgotPassword(ForgotPasswordRequest request, CancellationToken ct) =>
        Respond(await authService.ForgotPasswordAsync(request, ct));

    /// <summary>Step 2: checks the code (without using it up) before the user picks a new password.</summary>
    [HttpPost("verify-reset-code")]
    [AllowAnonymous]
    public async Task<ActionResult<ResponseCls<object>>> VerifyResetCode(VerifyResetCodeRequest request, CancellationToken ct) =>
        Respond(await authService.VerifyResetCodeAsync(request, ct));

    /// <summary>Step 3: sets the new password, uses up the code and signs the account out everywhere.</summary>
    [HttpPost("reset-password")]
    [AllowAnonymous]
    public async Task<ActionResult<ResponseCls<object>>> ResetPassword(ResetPasswordRequest request, CancellationToken ct) =>
        Respond(await authService.ResetPasswordAsync(request, ct));

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<ActionResult<ResponseCls<object>>> Login(LoginRequest request, CancellationToken ct)
    {
        var result = await authService.LoginAsync(request, ClientInfo(), ct);

        if (result.StatusCode == 403)
        {
            return Respond(ServiceResult<object>.Fail(403, result.Message,
                new VerificationRequiredResult(true, request.Email.Trim().ToLowerInvariant())));
        }

        return SessionResponse(result);
    }

    [HttpPost("refresh")]
    [AllowAnonymous]
    public async Task<ActionResult<ResponseCls<object>>> Refresh(CancellationToken ct)
    {
        var result = await authService.RefreshAsync(Request.Cookies[_security.RefreshCookieName], ClientInfo(), ct);
        if (!result.IsSuccess)
        {
            ClearRefreshCookie();
        }

        return SessionResponse(result);
    }

    [HttpPost("logout")]
    [AllowAnonymous]
    public async Task<ActionResult<ResponseCls<object>>> Logout(CancellationToken ct)
    {
        await authService.LogoutAsync(Request.Cookies[_security.RefreshCookieName], ct);
        ClearRefreshCookie();
        return Respond(ServiceResult<object>.Ok(null!, "You have been signed out."));
    }

    [HttpGet("me")]
    [Authorize(Policy = "UserOnly")]
    public async Task<ActionResult<ResponseCls<UserProfile>>> Me(CancellationToken ct) =>
        Respond(await authService.GetProfileAsync(User.GetUserId(), ct));

    private ActionResult<ResponseCls<object>> SessionResponse(ServiceResult<IssuedSession> result)
    {
        if (!result.IsSuccess || result.Value is null)
        {
            return Respond(ServiceResult<object>.Fail(result.StatusCode, result.Message));
        }

        Response.Cookies.Append(_security.RefreshCookieName, result.Value.RefreshToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = _security.RefreshCookieSecure,
            SameSite = SameSiteMode.Strict,
            Path = RefreshCookiePath,
            Expires = result.Value.RefreshExpiresAtUtc,
            IsEssential = true
        });

        return Respond(ServiceResult<object>.Ok(result.Value.Session, result.Message));
    }

    private void ClearRefreshCookie() =>
        Response.Cookies.Delete(_security.RefreshCookieName, new CookieOptions
        {
            HttpOnly = true,
            Secure = _security.RefreshCookieSecure,
            SameSite = SameSiteMode.Strict,
            Path = RefreshCookiePath
        });

    private ClientInfo ClientInfo() =>
        new(HttpContext.Connection.RemoteIpAddress?.ToString(), Request.Headers.UserAgent.ToString());
}
