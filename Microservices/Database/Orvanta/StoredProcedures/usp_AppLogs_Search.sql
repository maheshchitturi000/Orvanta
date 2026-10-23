SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO
-- Support lookup. A user reporting "Reference: 0HN7..." is found with @TraceId alone.
-- Example: EXEC dbo.usp_AppLogs_Search @TraceId = N'0HN7ABC:00000001';
--          EXEC dbo.usp_AppLogs_Search @ServiceName = N'auth-service', @LogLevel = N'Error', @Top = 50;
CREATE OR ALTER PROCEDURE dbo.usp_AppLogs_Search
    @ServiceName NVARCHAR(60)     = NULL,
    @LogLevel    NVARCHAR(12)     = NULL,
    @TraceId     NVARCHAR(64)     = NULL,
    @UserId      UNIQUEIDENTIFIER = NULL,
    @FromUtc     DATETIME2(3)     = NULL,
    @ToUtc       DATETIME2(3)     = NULL,
    @Top         INT              = 100
AS
BEGIN
    SET NOCOUNT ON;

    SELECT TOP (@Top)
           AppLogId, LoggedAtUtc, ServiceName, LogLevel, Category, Message, ExceptionType, ExceptionMessage,
           StackTrace, TraceId, RequestMethod, RequestPath, UserId, ClientIp
    FROM dbo.AppLogs
    WHERE (@ServiceName IS NULL OR ServiceName = @ServiceName)
      AND (@LogLevel IS NULL OR LogLevel = @LogLevel)
      AND (@TraceId IS NULL OR TraceId = @TraceId)
      AND (@UserId IS NULL OR UserId = @UserId)
      AND (@FromUtc IS NULL OR LoggedAtUtc >= @FromUtc)
      AND (@ToUtc IS NULL OR LoggedAtUtc < @ToUtc)
    ORDER BY LoggedAtUtc DESC
    OPTION (RECOMPILE);
END
GO
