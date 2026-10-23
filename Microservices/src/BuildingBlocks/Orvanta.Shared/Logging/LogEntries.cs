using Microsoft.AspNetCore.Http;
using Orvanta.Shared.Security;

namespace Orvanta.Shared.Logging;

/// <summary>Something waiting in <see cref="DatabaseLogQueue"/> to be written to the Orvanta database.</summary>
public abstract record DatabaseLogRecord(DateTime TimestampUtc, RequestInfo? Request);

/// <summary>Row of AppLogs.</summary>
public sealed record AppLogEntry(
    DateTime TimestampUtc,
    string Level,
    string? Category,
    string Message,
    string? ExceptionType,
    string? ExceptionMessage,
    string? StackTrace,
    RequestInfo? Request) : DatabaseLogRecord(TimestampUtc, Request);

/// <summary>Row of AuditLogs.</summary>
public sealed record AuditEntry(
    DateTime TimestampUtc,
    string Action,
    bool Success,
    Guid? UserId,
    string? UserEmail,
    string? EntityType,
    string? EntityId,
    int? StatusCode,
    int? DurationMs,
    string? Details,
    RequestInfo? Request) : DatabaseLogRecord(TimestampUtc, Request);

/// <summary>
/// Request context stored with log and audit rows. Only the path is kept, never the query string or
/// body, so OAuth codes, tokens and personal data don't end up in the logs.
/// </summary>
public sealed record RequestInfo(
    string TraceId,
    string Method,
    string Path,
    Guid? UserId,
    string? UserEmail,
    string? ClientIp,
    string? UserAgent)
{
    public static RequestInfo? From(HttpContext? context)
    {
        if (context is null)
        {
            return null;
        }

        Guid? userId = null;
        string? email = null;
        if (context.User.Identity?.IsAuthenticated == true)
        {
            userId = Guid.TryParse(context.User.FindFirst(OrvantaClaims.UserId)?.Value, out var id) ? id : null;
            email = context.User.FindFirst(OrvantaClaims.Email)?.Value;
        }

        return new RequestInfo(
            context.TraceIdentifier,
            context.Request.Method,
            context.Request.Path.Value ?? "/",
            userId,
            email,
            context.Connection.RemoteIpAddress?.ToString(),
            context.Request.Headers.UserAgent.ToString());
    }
}
