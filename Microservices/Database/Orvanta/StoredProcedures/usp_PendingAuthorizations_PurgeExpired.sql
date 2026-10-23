SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO
-- Housekeeping (schedule daily): removes OAuth "state" rows for sign-ins that were never completed.
CREATE OR ALTER PROCEDURE dbo.usp_PendingAuthorizations_PurgeExpired
AS
BEGIN
    SET NOCOUNT ON;

    DELETE FROM dbo.PendingAuthorizations
    WHERE ExpiresAtUtc < SYSUTCDATETIME();

    SELECT @@ROWCOUNT AS PendingAuthorizationsDeleted;
END
GO
