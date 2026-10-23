-- Audit trail: who did what, when, from where, and whether it worked.
-- Written by Orvanta.Shared (AuditMiddleware for signed-in changes, IAuditLogger for auth events)
-- through dbo.usp_AuditLogs_Insert. Request bodies are never stored.
IF OBJECT_ID(N'dbo.AuditLogs', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.AuditLogs
    (
        AuditLogId    BIGINT IDENTITY(1, 1) NOT NULL,
        OccurredAtUtc DATETIME2(3)     NOT NULL CONSTRAINT DF_AuditLogs_OccurredAtUtc DEFAULT (SYSUTCDATETIME()),
        ServiceName   NVARCHAR(60)     NOT NULL,
        Action        NVARCHAR(100)    NOT NULL,
        Outcome       NVARCHAR(10)     NOT NULL,
        UserId        UNIQUEIDENTIFIER NULL,
        UserEmail     NVARCHAR(254)    NULL,
        EntityType    NVARCHAR(100)    NULL,
        EntityId      NVARCHAR(100)    NULL,
        HttpMethod    NVARCHAR(10)     NULL,
        RequestPath   NVARCHAR(500)    NULL,
        StatusCode    INT              NULL,
        DurationMs    INT              NULL,
        TraceId       NVARCHAR(64)     NULL,
        ClientIp      NVARCHAR(64)     NULL,
        UserAgent     NVARCHAR(512)    NULL,
        Details       NVARCHAR(2000)   NULL,
        CONSTRAINT PK_AuditLogs PRIMARY KEY CLUSTERED (AuditLogId),
        CONSTRAINT CK_AuditLogs_Outcome CHECK (Outcome IN (N'Success', N'Failure'))
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_AuditLogs_OccurredAtUtc' AND object_id = OBJECT_ID(N'dbo.AuditLogs'))
    CREATE INDEX IX_AuditLogs_OccurredAtUtc ON dbo.AuditLogs (OccurredAtUtc DESC);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_AuditLogs_UserId_OccurredAtUtc' AND object_id = OBJECT_ID(N'dbo.AuditLogs'))
    CREATE INDEX IX_AuditLogs_UserId_OccurredAtUtc ON dbo.AuditLogs (UserId, OccurredAtUtc DESC) WHERE UserId IS NOT NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_AuditLogs_Action_OccurredAtUtc' AND object_id = OBJECT_ID(N'dbo.AuditLogs'))
    CREATE INDEX IX_AuditLogs_Action_OccurredAtUtc ON dbo.AuditLogs (Action, OccurredAtUtc DESC);
GO
