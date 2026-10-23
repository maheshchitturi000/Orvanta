using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Orvanta.Shared.Logging;

namespace Orvanta.Shared.Auditing;

/// <summary>
/// Records business/security events in AuditLogs. Use it for events the
/// <see cref="AuditMiddleware"/> can't attribute on its own, e.g. sign-in (no token yet) or a failed login.
/// </summary>
public interface IAuditLogger
{
    void Log(
        string action,
        bool success,
        Guid? userId = null,
        string? userEmail = null,
        string? entityType = null,
        string? entityId = null,
        string? details = null);
}

public sealed class AuditLogger(DatabaseLogQueue queue, IHttpContextAccessor httpContextAccessor, ILogger<AuditLogger> logger)
    : IAuditLogger
{
    public void Log(
        string action,
        bool success,
        Guid? userId = null,
        string? userEmail = null,
        string? entityType = null,
        string? entityId = null,
        string? details = null)
    {
        // Also in the log files, so the trail survives a logs-database outage.
        logger.LogInformation("Audit {Action} {Outcome} user {UserId} {EntityType} {EntityId}",
            action, success ? "Success" : "Failure", userId, entityType, entityId);

        queue.Enqueue(new AuditEntry(
            DateTime.UtcNow, action, success, userId, userEmail, entityType, entityId,
            StatusCode: null, DurationMs: null, details, RequestInfo.From(httpContextAccessor.HttpContext)));
    }
}
