SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO
-- Called by Orvanta.Shared DatabaseLogWriter. Values are truncated to the column sizes so a long
-- message can never make logging itself fail.
CREATE OR ALTER PROCEDURE dbo.usp_AppLogs_Insert
    @LoggedAtUtc      DATETIME2(3),
    @ServiceName      NVARCHAR(60),
    @Environment      NVARCHAR(30)     = NULL,
    @MachineName      NVARCHAR(100)    = NULL,
    @LogLevel         NVARCHAR(12),
    @Category         NVARCHAR(256)    = NULL,
    @Message          NVARCHAR(MAX),
    @ExceptionType    NVARCHAR(256)    = NULL,
    @ExceptionMessage NVARCHAR(MAX)    = NULL,
    @StackTrace       NVARCHAR(MAX)    = NULL,
    @TraceId          NVARCHAR(64)     = NULL,
    @RequestMethod    NVARCHAR(10)     = NULL,
    @RequestPath      NVARCHAR(MAX)    = NULL,
    @UserId           UNIQUEIDENTIFIER = NULL,
    @ClientIp         NVARCHAR(64)     = NULL,
    @UserAgent        NVARCHAR(MAX)    = NULL
AS
BEGIN
    SET NOCOUNT ON;

    INSERT INTO dbo.AppLogs
        (LoggedAtUtc, ServiceName, Environment, MachineName, LogLevel, Category, Message,
         ExceptionType, ExceptionMessage, StackTrace, TraceId, RequestMethod, RequestPath,
         UserId, ClientIp, UserAgent)
    VALUES
        (ISNULL(@LoggedAtUtc, SYSUTCDATETIME()), @ServiceName, @Environment, @MachineName, @LogLevel, @Category,
         LEFT(@Message, 4000), @ExceptionType, LEFT(@ExceptionMessage, 4000), @StackTrace, @TraceId,
         @RequestMethod, LEFT(@RequestPath, 500), @UserId, @ClientIp, LEFT(@UserAgent, 512));
END
GO
