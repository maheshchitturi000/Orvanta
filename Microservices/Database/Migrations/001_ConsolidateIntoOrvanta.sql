/*
    One-time data move (2026-10): the five per-service databases become the single Orvanta database.

        OrvantaAuth.dbo.*  -> Orvanta (Users, EmailOtps, RefreshTokens)
        OrvantaFood.dbo.*  -> Orvanta (ProviderConnections, SavedAddresses, ...)
        OrvantaJobs.dbo.*  -> Orvanta (CandidateProfiles, JobPostings, ...)
        OrvantaDb.dbo.Conversations/Messages -> Orvanta
        OrvantaLogs.dbo.*  -> Orvanta (AppLogs, AuditLogs)

    Run against Orvanta after the schema exists (Deploy-Database.ps1 -IncludeMigrations does both).
    - Idempotent: rows already present (same primary key) are skipped, so it can be re-run, e.g. once more
      right after stopping the services so nothing written in between is missed.
    - Source databases that don't exist are skipped (fresh installs).
    - The old databases are only read, never changed or dropped. Drop them by hand once Orvanta is verified.
    - Not moved: legacy tables of the retired Orvanta.IntegrationService in OrvantaDb.
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

CREATE OR ALTER PROCEDURE #CopyTable
    @SourceDb     sysname,
    @Table        sysname,
    @TargetSchema sysname
AS
BEGIN
    SET NOCOUNT ON;

    IF DB_ID(@SourceDb) IS NULL
    BEGIN
        PRINT CONCAT('skip ', @SourceDb, '.dbo.', @Table, ' (database not found)');
        RETURN;
    END

    DECLARE @target nvarchar(300) = QUOTENAME(@TargetSchema) + N'.' + QUOTENAME(@Table);
    DECLARE @source nvarchar(400) = QUOTENAME(@SourceDb) + N'.dbo.' + QUOTENAME(@Table);
    DECLARE @targetId int = OBJECT_ID(@target);

    -- Columns of the target table (the source has the same ones), and its primary key for the "already copied" check.
    DECLARE @columns nvarchar(max) =
        (SELECT STRING_AGG(CAST(QUOTENAME(name) AS nvarchar(max)), N', ') WITHIN GROUP (ORDER BY column_id)
         FROM sys.columns WHERE object_id = @targetId AND is_computed = 0);

    DECLARE @keyMatch nvarchar(max) =
        (SELECT STRING_AGG(CAST(N't.' + QUOTENAME(c.name) + N' = s.' + QUOTENAME(c.name) AS nvarchar(max)), N' AND ')
         FROM sys.indexes i
         JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
         JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
         WHERE i.object_id = @targetId AND i.is_primary_key = 1);

    DECLARE @hasIdentity bit = CASE WHEN EXISTS (SELECT 1 FROM sys.identity_columns WHERE object_id = @targetId) THEN 1 ELSE 0 END;

    DECLARE @sql nvarchar(max) =
          CASE WHEN @hasIdentity = 1 THEN N'SET IDENTITY_INSERT ' + @target + N' ON; ' ELSE N'' END
        + N'INSERT INTO ' + @target + N' (' + @columns + N') '
        + N'SELECT ' + @columns + N' FROM ' + @source + N' AS s '
        + N'WHERE NOT EXISTS (SELECT 1 FROM ' + @target + N' AS t WHERE ' + @keyMatch + N'); '
        + N'PRINT CONCAT(''' + @SourceDb + N'.dbo.' + @Table + N' -> ' + @TargetSchema + N'.' + @Table + N': '', @@ROWCOUNT, '' row(s) copied''); '
        + CASE WHEN @hasIdentity = 1 THEN N'SET IDENTITY_INSERT ' + @target + N' OFF;' ELSE N'' END;

    EXEC sys.sp_executesql @sql;
END
GO

BEGIN TRANSACTION;

-- Parents before children (foreign keys).
EXEC #CopyTable N'OrvantaAuth', N'Users', N'dbo';
EXEC #CopyTable N'OrvantaAuth', N'EmailOtps', N'dbo';
EXEC #CopyTable N'OrvantaAuth', N'RefreshTokens', N'dbo';

EXEC #CopyTable N'OrvantaFood', N'OAuthClientRegistrations', N'dbo';
EXEC #CopyTable N'OrvantaFood', N'PendingAuthorizations', N'dbo';
EXEC #CopyTable N'OrvantaFood', N'ProviderConnections', N'dbo';
EXEC #CopyTable N'OrvantaFood', N'SavedAddresses', N'dbo';
EXEC #CopyTable N'OrvantaFood', N'FoodOrders', N'dbo';

EXEC #CopyTable N'OrvantaJobs', N'CandidateProfiles', N'dbo';
EXEC #CopyTable N'OrvantaJobs', N'JobPreferences', N'dbo';
EXEC #CopyTable N'OrvantaJobs', N'JobSourceConnections', N'dbo';
EXEC #CopyTable N'OrvantaJobs', N'JobPostings', N'dbo';
EXEC #CopyTable N'OrvantaJobs', N'JobMatches', N'dbo';
EXEC #CopyTable N'OrvantaJobs', N'JobApplications', N'dbo';

EXEC #CopyTable N'OrvantaDb', N'Conversations', N'dbo';
EXEC #CopyTable N'OrvantaDb', N'Messages', N'dbo';

EXEC #CopyTable N'OrvantaLogs', N'AppLogs', N'dbo';
EXEC #CopyTable N'OrvantaLogs', N'AuditLogs', N'dbo';

COMMIT TRANSACTION;
GO

DROP PROCEDURE IF EXISTS #CopyTable;
GO
