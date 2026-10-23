SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO
-- Activity of one user since @SinceUtc.
-- Example: SELECT * FROM dbo.ufn_AuditLogs_ByUser(@UserId, DATEADD(DAY, -7, SYSUTCDATETIME())) ORDER BY OccurredAtUtc DESC;
CREATE OR ALTER FUNCTION dbo.ufn_AuditLogs_ByUser (@UserId UNIQUEIDENTIFIER, @SinceUtc DATETIME2(3))
RETURNS TABLE
AS
RETURN
    SELECT AuditLogId, OccurredAtUtc, ServiceName, Action, Outcome, EntityType, EntityId,
           HttpMethod, RequestPath, StatusCode, DurationMs, TraceId, ClientIp, Details
    FROM dbo.AuditLogs
    WHERE UserId = @UserId
      AND OccurredAtUtc >= @SinceUtc;
GO
