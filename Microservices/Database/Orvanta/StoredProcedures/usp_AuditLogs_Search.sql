SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO
-- Example: EXEC dbo.usp_AuditLogs_Search @UserId = '...', @FromUtc = '2026-10-01';
--          EXEC dbo.usp_AuditLogs_Search @Action = N'Auth.LoginFailed', @Top = 50;
CREATE OR ALTER PROCEDURE dbo.usp_AuditLogs_Search
    @UserId      UNIQUEIDENTIFIER = NULL,
    @Action      NVARCHAR(100)    = NULL,
    @Outcome     NVARCHAR(10)     = NULL,
    @ServiceName NVARCHAR(60)     = NULL,
    @FromUtc     DATETIME2(3)     = NULL,
    @ToUtc       DATETIME2(3)     = NULL,
    @Top         INT              = 100
AS
BEGIN
    SET NOCOUNT ON;

    SELECT TOP (@Top)
           AuditLogId, OccurredAtUtc, ServiceName, Action, Outcome, UserId, UserEmail, EntityType, EntityId,
           HttpMethod, RequestPath, StatusCode, DurationMs, TraceId, ClientIp, UserAgent, Details
    FROM dbo.AuditLogs
    WHERE (@UserId IS NULL OR UserId = @UserId)
      AND (@Action IS NULL OR Action = @Action)
      AND (@Outcome IS NULL OR Outcome = @Outcome)
      AND (@ServiceName IS NULL OR ServiceName = @ServiceName)
      AND (@FromUtc IS NULL OR OccurredAtUtc >= @FromUtc)
      AND (@ToUtc IS NULL OR OccurredAtUtc < @ToUtc)
    ORDER BY OccurredAtUtc DESC
    OPTION (RECOMPILE);
END
GO
