SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO
-- Housekeeping (schedule daily). Refresh tokens rotate every few minutes, so expired and revoked rows pile
-- up quickly. Rows are kept @RetentionDays past expiry/revocation for incident investigation.
-- Example: EXEC dbo.usp_Auth_PurgeExpired @RetentionDays = 7;
CREATE OR ALTER PROCEDURE dbo.usp_Auth_PurgeExpired
    @RetentionDays INT = 7,
    @BatchSize     INT = 5000
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @cutoff DATETIME2 = DATEADD(DAY, -@RetentionDays, SYSUTCDATETIME());
    DECLARE @deleted INT = 1, @tokens INT = 0, @otps INT = 0;

    WHILE @deleted > 0
    BEGIN
        DELETE TOP (@BatchSize) FROM dbo.RefreshTokens
        WHERE ExpiresAtUtc < @cutoff OR RevokedAtUtc < @cutoff;
        SET @deleted = @@ROWCOUNT;
        SET @tokens += @deleted;
    END

    SET @deleted = 1;
    WHILE @deleted > 0
    BEGIN
        DELETE TOP (@BatchSize) FROM dbo.EmailOtps
        WHERE ExpiresAtUtc < @cutoff;
        SET @deleted = @@ROWCOUNT;
        SET @otps += @deleted;
    END

    SELECT @tokens AS RefreshTokensDeleted, @otps AS EmailOtpsDeleted;
END
GO
