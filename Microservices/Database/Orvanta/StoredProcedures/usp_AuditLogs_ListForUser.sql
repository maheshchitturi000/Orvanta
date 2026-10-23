SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO
-- The signed-in user's own activity for the Dashboard (GET /api/auth/activity), newest first.
-- Automatic background events (silent token refreshes) are left out: they aren't something the user did.
-- Example: EXEC dbo.usp_AuditLogs_ListForUser @UserId = '...', @Top = 50;
CREATE OR ALTER PROCEDURE dbo.usp_AuditLogs_ListForUser
    @UserId UNIQUEIDENTIFIER,
    @Top    INT = 50
AS
BEGIN
    SET NOCOUNT ON;

    SELECT TOP (@Top)
           AuditLogId, OccurredAtUtc, ServiceName, Action, Outcome, EntityType, EntityId
    FROM dbo.AuditLogs
    WHERE UserId = @UserId
      AND Action NOT IN (N'Auth.SessionRefreshed', N'Auth.Refresh')
    ORDER BY OccurredAtUtc DESC;
END
GO
