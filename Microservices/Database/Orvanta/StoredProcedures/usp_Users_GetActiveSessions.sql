SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO
-- Support: list where a user is currently signed in.
-- Example: EXEC dbo.usp_Users_GetActiveSessions @Email = N'someone@example.com';
CREATE OR ALTER PROCEDURE dbo.usp_Users_GetActiveSessions
    @Email NVARCHAR(254)
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @UserId UNIQUEIDENTIFIER =
        (SELECT Id FROM dbo.Users WHERE Email = LOWER(LTRIM(RTRIM(@Email))));

    SELECT s.FamilyId, s.LastRefreshedAtUtc, s.ExpiresAtUtc, s.CreatedByIp, s.UserAgent
    FROM dbo.ufn_RefreshTokens_ActiveByUser(@UserId, SYSUTCDATETIME()) AS s
    ORDER BY s.LastRefreshedAtUtc DESC;
END
GO
