SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO
-- Orders and spend per provider for one user since @SinceUtc.
-- Example: SELECT * FROM dbo.ufn_FoodOrders_UserSummary(@UserId, DATEADD(MONTH, -1, SYSUTCDATETIME()));
CREATE OR ALTER FUNCTION dbo.ufn_FoodOrders_UserSummary (@UserId UNIQUEIDENTIFIER, @SinceUtc DATETIME2)
RETURNS TABLE
AS
RETURN
    SELECT Provider,
           COUNT(*)          AS Orders,
           SUM(TotalAmount)  AS TotalSpent,
           MAX(CreatedAtUtc) AS LastOrderAtUtc
    FROM dbo.FoodOrders
    WHERE UserId = @UserId
      AND CreatedAtUtc >= @SinceUtc
    GROUP BY Provider;
GO
