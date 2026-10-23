-- Application log: warnings and errors from every Orvanta service.
-- Written by Orvanta.Shared (Serilog sink -> dbo.usp_AppLogs_Insert). Unhandled exceptions caught by
-- GlobalExceptionMiddleware land here with the request context and the trace reference shown to the user.
IF OBJECT_ID(N'dbo.AppLogs', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.AppLogs
    (
        AppLogId         BIGINT IDENTITY(1, 1) NOT NULL,
        LoggedAtUtc      DATETIME2(3)     NOT NULL CONSTRAINT DF_AppLogs_LoggedAtUtc DEFAULT (SYSUTCDATETIME()),
        ServiceName      NVARCHAR(60)     NOT NULL,
        Environment      NVARCHAR(30)     NULL,
        MachineName      NVARCHAR(100)    NULL,
        LogLevel         NVARCHAR(12)     NOT NULL,
        Category         NVARCHAR(256)    NULL,
        Message          NVARCHAR(4000)   NOT NULL,
        ExceptionType    NVARCHAR(256)    NULL,
        ExceptionMessage NVARCHAR(4000)   NULL,
        StackTrace       NVARCHAR(MAX)    NULL,
        TraceId          NVARCHAR(64)     NULL,
        RequestMethod    NVARCHAR(10)     NULL,
        RequestPath      NVARCHAR(500)    NULL,
        UserId           UNIQUEIDENTIFIER NULL,
        ClientIp         NVARCHAR(64)     NULL,
        UserAgent        NVARCHAR(512)    NULL,
        CONSTRAINT PK_AppLogs PRIMARY KEY CLUSTERED (AppLogId),
        CONSTRAINT CK_AppLogs_LogLevel CHECK (LogLevel IN (N'Verbose', N'Debug', N'Information', N'Warning', N'Error', N'Fatal'))
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_AppLogs_LoggedAtUtc' AND object_id = OBJECT_ID(N'dbo.AppLogs'))
    CREATE INDEX IX_AppLogs_LoggedAtUtc ON dbo.AppLogs (LoggedAtUtc DESC);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_AppLogs_Service_Level_LoggedAtUtc' AND object_id = OBJECT_ID(N'dbo.AppLogs'))
    CREATE INDEX IX_AppLogs_Service_Level_LoggedAtUtc ON dbo.AppLogs (ServiceName, LogLevel, LoggedAtUtc DESC);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_AppLogs_TraceId' AND object_id = OBJECT_ID(N'dbo.AppLogs'))
    CREATE INDEX IX_AppLogs_TraceId ON dbo.AppLogs (TraceId) WHERE TraceId IS NOT NULL;
GO
