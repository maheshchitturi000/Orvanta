SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO
-- Signed-in sessions of a user: the newest live refresh token of each session family (one per login/device).
-- Example: SELECT * FROM dbo.ufn_RefreshTokens_ActiveByUser(@UserId, SYSUTCDATETIME());
CREATE OR ALTER FUNCTION dbo.ufn_RefreshTokens_ActiveByUser (@UserId UNIQUEIDENTIFIER, @NowUtc DATETIME2)
RETURNS TABLE
AS
RETURN
    SELECT FamilyId, CreatedAtUtc AS LastRefreshedAtUtc, ExpiresAtUtc, CreatedByIp, UserAgent
    FROM
    (
        SELECT FamilyId, CreatedAtUtc, ExpiresAtUtc, CreatedByIp, UserAgent,
               ROW_NUMBER() OVER (PARTITION BY FamilyId ORDER BY CreatedAtUtc DESC) AS rn
        FROM dbo.RefreshTokens
        WHERE UserId = @UserId
          AND RevokedAtUtc IS NULL
          AND ExpiresAtUtc > @NowUtc
    ) AS live
    WHERE rn = 1;
GO
