SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO
-- Retention job (schedule daily with SQL Agent / Azure Automation). Deletes in small batches so the
-- log tables stay writable while it runs.
-- Example: EXEC dbo.usp_Logs_Purge @AppLogRetentionDays = 30, @AuditLogRetentionDays = 365;
CREATE OR ALTER PROCEDURE dbo.usp_Logs_Purge
    @AppLogRetentionDays   INT = 90,
    @AuditLogRetentionDays INT = 365,
    @BatchSize             INT = 5000
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @appCutoff   DATETIME2(3) = DATEADD(DAY, -@AppLogRetentionDays, SYSUTCDATETIME());
    DECLARE @auditCutoff DATETIME2(3) = DATEADD(DAY, -@AuditLogRetentionDays, SYSUTCDATETIME());
    DECLARE @deleted INT = 1, @appTotal INT = 0, @auditTotal INT = 0;

    WHILE @deleted > 0
    BEGIN
        DELETE TOP (@BatchSize) FROM dbo.AppLogs WHERE LoggedAtUtc < @appCutoff;
        SET @deleted = @@ROWCOUNT;
        SET @appTotal += @deleted;
    END

    SET @deleted = 1;
    WHILE @deleted > 0
    BEGIN
        DELETE TOP (@BatchSize) FROM dbo.AuditLogs WHERE OccurredAtUtc < @auditCutoff;
        SET @deleted = @@ROWCOUNT;
        SET @auditTotal += @deleted;
    END

    SELECT @appTotal AS AppLogsDeleted, @auditTotal AS AuditLogsDeleted;
END
GO
