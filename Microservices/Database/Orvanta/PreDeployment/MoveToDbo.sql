/*
    One-time move (2026-10): tables lived in per-area schemas (auth.Users, logs.AppLogs, ...). Every object now
    uses the default schema, so tables are simply Users, AppLogs, ...

    Runs before Tables/ (Deploy-Database.ps1), so existing tables keep their data instead of empty copies being
    created next to them. Idempotent: on a fresh database or a second run there is nothing to move.
    - Tables are transferred with their data, keys, indexes and constraints (ALTER SCHEMA ... TRANSFER).
    - Old procedures and functions are dropped; the deploy recreates them under the same names in dbo.
    - The emptied schemas are removed.
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @oldSchemas TABLE (name sysname PRIMARY KEY);
INSERT INTO @oldSchemas VALUES (N'auth'), (N'food'), (N'jobs'), (N'chat'), (N'logs');

DECLARE @sql nvarchar(max) = N'';

-- Procedures and functions first: they reference the old schema-qualified tables.
SELECT @sql += CASE o.type WHEN 'P' THEN N'DROP PROCEDURE ' ELSE N'DROP FUNCTION ' END
             + QUOTENAME(s.name) + N'.' + QUOTENAME(o.name) + N';' + CHAR(10)
FROM sys.objects o
JOIN sys.schemas s ON s.schema_id = o.schema_id
WHERE s.name IN (SELECT name FROM @oldSchemas)
  AND o.type IN ('P', 'FN', 'IF', 'TF');

-- Tables: move unless a dbo table with that name already exists (never overwrite).
SELECT @sql += N'ALTER SCHEMA dbo TRANSFER ' + QUOTENAME(s.name) + N'.' + QUOTENAME(t.name) + N';' + CHAR(10)
FROM sys.tables t
JOIN sys.schemas s ON s.schema_id = t.schema_id
WHERE s.name IN (SELECT name FROM @oldSchemas)
  AND OBJECT_ID(N'dbo.' + QUOTENAME(t.name), N'U') IS NULL;

IF @sql <> N''
BEGIN
    BEGIN TRANSACTION;
    PRINT @sql;
    EXEC sys.sp_executesql @sql;
    COMMIT TRANSACTION;
END

-- Drop schemas that are now empty.
DECLARE @schema sysname;
DECLARE schemas_cursor CURSOR LOCAL FAST_FORWARD FOR
    SELECT s.name FROM sys.schemas s
    WHERE s.name IN (SELECT name FROM @oldSchemas)
      AND NOT EXISTS (SELECT 1 FROM sys.objects o WHERE o.schema_id = s.schema_id);
OPEN schemas_cursor;
FETCH NEXT FROM schemas_cursor INTO @schema;
WHILE @@FETCH_STATUS = 0
BEGIN
    SET @sql = N'DROP SCHEMA ' + QUOTENAME(@schema);
    EXEC sys.sp_executesql @sql;
    PRINT N'Dropped empty schema ' + @schema;
    FETCH NEXT FROM schemas_cursor INTO @schema;
END
CLOSE schemas_cursor;
DEALLOCATE schemas_cursor;
GO
