SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO
-- Warning/error counts per service and level since @SinceUtc (health dashboards, daily checks).
-- Example: SELECT * FROM dbo.ufn_AppLogs_ErrorSummary(DATEADD(DAY, -1, SYSUTCDATETIME())) ORDER BY Total DESC;
CREATE OR ALTER FUNCTION dbo.ufn_AppLogs_ErrorSummary (@SinceUtc DATETIME2(3))
RETURNS TABLE
AS
RETURN
    SELECT ServiceName,
           LogLevel,
           COUNT_BIG(*)     AS Total,
           MIN(LoggedAtUtc) AS FirstAtUtc,
           MAX(LoggedAtUtc) AS LastAtUtc
    FROM dbo.AppLogs
    WHERE LoggedAtUtc >= @SinceUtc
      AND LogLevel IN (N'Warning', N'Error', N'Fatal')
    GROUP BY ServiceName, LogLevel;
GO
