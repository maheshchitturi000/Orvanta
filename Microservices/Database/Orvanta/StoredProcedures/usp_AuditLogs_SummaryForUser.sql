SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO
-- Dashboard charts (GET /api/auth/activity/summary): the user's actions per local day and area for the
-- last @Days days. @UtcOffsetMinutes is the browser's offset (IST = 330) so "today" matches the user's clock.
-- Area = the part of the action before the dot (Auth, Food, Integrations, Jobs, Ondc, ...).
-- Silent token refreshes are left out: they aren't something the user did.
-- Example: EXEC dbo.usp_AuditLogs_SummaryForUser @UserId = '...', @Days = 14, @UtcOffsetMinutes = 330;
CREATE OR ALTER PROCEDURE dbo.usp_AuditLogs_SummaryForUser
    @UserId           UNIQUEIDENTIFIER,
    @Days             INT = 14,
    @UtcOffsetMinutes INT = 0
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @localToday DATE = CAST(DATEADD(MINUTE, @UtcOffsetMinutes, SYSUTCDATETIME()) AS DATE);
    DECLARE @fromLocal  DATE = DATEADD(DAY, 1 - @Days, @localToday);
    DECLARE @fromUtc    DATETIME2(3) = DATEADD(MINUTE, -@UtcOffsetMinutes, CAST(@fromLocal AS DATETIME2(3)));

    SELECT CAST(DATEADD(MINUTE, @UtcOffsetMinutes, OccurredAtUtc) AS DATE) AS Day,
           LEFT(Action, CHARINDEX(N'.', Action + N'.') - 1)                AS Area,
           COUNT(*)                                                       AS Total,
           SUM(CASE WHEN Outcome = N'Failure' THEN 1 ELSE 0 END)          AS Failed
    FROM dbo.AuditLogs
    WHERE UserId = @UserId
      AND OccurredAtUtc >= @fromUtc
      AND Action NOT IN (N'Auth.SessionRefreshed', N'Auth.Refresh')
    GROUP BY CAST(DATEADD(MINUTE, @UtcOffsetMinutes, OccurredAtUtc) AS DATE),
             LEFT(Action, CHARINDEX(N'.', Action + N'.') - 1);
END
GO
