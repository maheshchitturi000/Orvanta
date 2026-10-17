using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Options;

namespace Orvanta.AuthService.Notifications;

public interface IEmailSender
{
    Task SendAsync(string toEmail, string subject, string htmlBody, CancellationToken ct = default);
}

/// <summary>Email could not be delivered. The message is safe to log; it never contains the password.</summary>
public sealed class EmailDeliveryException(string message, Exception inner) : Exception(message, inner);

public sealed class SmtpOptions
{
    public const string SectionName = "Smtp";

    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 587;
    public bool EnableSsl { get; set; } = true;
    public string UserName { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string FromEmail { get; set; } = "no-reply@orvanta.ai";
    public string FromName { get; set; } = "Orvanta AI";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Host);
}

public sealed class SmtpEmailSender(IOptions<SmtpOptions> options, ILogger<SmtpEmailSender> logger) : IEmailSender
{
    private readonly SmtpOptions _options = options.Value;

    public async Task SendAsync(string toEmail, string subject, string htmlBody, CancellationToken ct = default)
    {
        try
        {
            await SendCoreAsync(toEmail, subject, htmlBody, ct);
            logger.LogInformation("Email sent to {To} via {Host}", toEmail, _options.Host);
        }
        catch (Exception ex) when (ex is SmtpException or InvalidOperationException or FormatException or IOException)
        {
            var reason = ex is SmtpException smtpEx
                ? $"SMTP status {smtpEx.StatusCode}: {smtpEx.Message}{(smtpEx.InnerException is null ? "" : $" ({smtpEx.InnerException.Message})")}"
                : ex.Message;

            // Common causes: wrong app password (5.7.0 / 5.7.8), FromEmail differs from the login account,
            // port/SSL mismatch, or the network blocking outbound SMTP.
            logger.LogError(ex, "Email to {To} FAILED via {Host}:{Port} as {User}. Reason: {Reason}",
                toEmail, _options.Host, _options.Port, _options.UserName, reason);

            throw new EmailDeliveryException(reason, ex);
        }
    }

    private async Task SendCoreAsync(string toEmail, string subject, string htmlBody, CancellationToken ct)
    {
        using var message = new MailMessage
        {
            From = new MailAddress(_options.FromEmail, _options.FromName),
            Subject = subject,
            Body = htmlBody,
            IsBodyHtml = true
        };
        message.To.Add(toEmail);

        using var client = new SmtpClient(_options.Host, _options.Port)
        {
            EnableSsl = _options.EnableSsl,
            Credentials = string.IsNullOrEmpty(_options.UserName)
                ? CredentialCache.DefaultNetworkCredentials
                : new NetworkCredential(_options.UserName, _options.Password)
        };

        await client.SendMailAsync(message, ct);
    }
}

/// <summary>
/// Used when SMTP is not configured (local development). Writes the email to
/// the service log so the OTP can be read from the AuthService console.
/// </summary>
public sealed class DevelopmentEmailSender(ILogger<DevelopmentEmailSender> logger) : IEmailSender
{
    public Task SendAsync(string toEmail, string subject, string htmlBody, CancellationToken ct = default)
    {
        logger.LogWarning("[DEV EMAIL - not sent, SMTP not configured] To: {To} | Subject: {Subject}", toEmail, subject);
        return Task.CompletedTask;
    }
}
