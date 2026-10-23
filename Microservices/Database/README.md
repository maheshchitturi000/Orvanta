# Orvanta database

Orvanta's data lives in **one SQL Server database: `Orvanta`**. Tables use plain names: `Users`, `AppLogs`,
`Conversations`... (the default `dbo` schema, so `SELECT * FROM AppLogs` just works).

| Area | Tables | Used by |
|---|---|---|
| Account | `Users`, `EmailOtps` (sign-up and password-reset codes), `RefreshTokens` (sessions) | Orvanta.AuthService (EF Core) |
| Food | `ProviderConnections` (Swiggy/Zomato tokens), `SavedAddresses`, `FoodOrders`, `PendingAuthorizations`, `OAuthClientRegistrations` | Orvanta.FoodService (EF Core) |
| ONDC | `OndcSearches`, `OndcCatalogItems` (dishes sellers returned), `OndcOrders`, `OndcMessages` (every signed message in and out) | Orvanta.FoodService (Dapper + `usp_Ondc*` procedures) |
| Jobs | `CandidateProfiles` (CV), `JobPreferences`, `JobPostings`, `JobMatches`, `JobApplications`, `JobSourceConnections` | Orvanta.Jobs.Core (EF Core) |
| Chat | `Conversations`, `Messages` (AI Assistant history) | Orvanta.ConversationService (Dapper + procedures) |
| Logs | `AppLogs` (errors), `AuditLogs` (who did what) | Orvanta.Shared, every service |

Every service connects with the same connection string, **`ConnectionStrings:Orvanta`**.

```
Database/
├─ Orvanta.Database.csproj                  # shows these scripts in Visual Studio (builds nothing)
├─ Deploy-Database.ps1                      # creates/updates Orvanta (idempotent, safe to re-run)
├─ Orvanta/
│  ├─ PreDeployment/MoveToDbo.sql           # one-time structural move, run first (idempotent)
│  ├─ Tables/<Table>.sql                    # e.g. Users.sql: table + constraints + its indexes
│  ├─ Functions/<ufn_Name>.sql              # e.g. ufn_AppLogs_ErrorSummary.sql
│  └─ StoredProcedures/<usp_Name>.sql       # e.g. usp_AppLogs_Insert.sql
└─ Migrations/
   └─ 001_ConsolidateIntoOrvanta.sql        # one-time copy from the old per-service databases
```

## Deploy

```powershell
powershell -ExecutionPolicy Bypass -File .\Microservices\Database\Deploy-Database.ps1                     # localhost\SQLEXPRESS
.\Microservices\Database\Deploy-Database.ps1 -Server "myserver" -UserName sa -Password "..."
.\Microservices\Database\Deploy-Database.ps1 -IncludeMigrations                                          # also run Migrations/
```

`run-all.ps1` runs it before starting the services. Order: PreDeployment → Tables → Functions → StoredProcedures.
**Each file is named after the object it creates.** Table scripts are ordered automatically by their foreign keys
(the deploy script reads each table's `REFERENCES`, so `Users` runs before `EmailOtps`). A missing database is
created with `AUTO_CLOSE OFF`.

**Services never create tables themselves** (no `EnsureCreated`): with one shared database, the first service to
start would otherwise create only its own tables. If a service reports `Invalid object name 'Users'`, run the
deploy script.

## Rules for scripts

- **Plain names in dbo.** Create objects as `dbo.<Name>` (explicit `dbo.` in scripts and code is good practice;
  queries can use just the name). Names must be unique across the whole database.
- **Idempotent.** Tables use `IF OBJECT_ID(...) IS NULL`, indexes `IF NOT EXISTS (sys.indexes ...)`,
  functions and procedures `CREATE OR ALTER`. A script must succeed on an empty database and on a live one.
- **Changing an existing table** (new column etc.): add a guarded `ALTER TABLE` at the end of that table's file
  (`IF COL_LENGTH(N'dbo.Users', N'NewColumn') IS NULL ALTER TABLE ...`). Never drop and re-create.
  Example: the `Purpose` column at the end of `EmailOtps.sql`.
- **EF-owned tables** (account, food, jobs): the C# model is the source of truth. When it changes, update the table
  file in the same commit.
- Procedures start with `SET ANSI_NULLS ON; SET QUOTED_IDENTIFIER ON;` and `SET NOCOUNT ON`; multi-statement
  writes use `SET XACT_ABORT ON` + a transaction.
- Large deletes run in batches (`DELETE TOP (@BatchSize)` loop) so tables stay writable.

## History of the layout

1. Each service had its own database (OrvantaAuth, OrvantaFood, OrvantaJobs, OrvantaDb, OrvantaLogs).
   `Migrations/001_ConsolidateIntoOrvanta.sql` copies all their data into `Orvanta` (safe to re-run; the old
   databases are only read, and can be dropped by hand once Orvanta is verified).
2. Tables briefly lived in per-area schemas (`auth.Users`, `logs.AppLogs`...).
   `PreDeployment/MoveToDbo.sql` moves them, with their data, to plain names and removes those schemas.

The legacy tables of the retired `Orvanta.IntegrationService` (in OrvantaDb) are not moved.

## Application logs and audit trail

Written asynchronously by `Orvanta.Shared` (a background queue, so logging never slows or breaks a request).
If the database is unreachable, entries still go to the rolling files in `Microservices/logs`.
Times are stored in UTC (`LoggedAtUtc`, `OccurredAtUtc`).

| Table | What goes in | Written by |
|---|---|---|
| `AppLogs` | Every **Warning/Error/Fatal** log from any service, including every unhandled exception caught by `GlobalExceptionMiddleware`, with request method/path, user id, client IP and the trace id | Serilog sink → `usp_AppLogs_Insert` |
| `AuditLogs` | **Who did what**: every change (POST/PUT/PATCH/DELETE) by a signed-in user, plus auth events (register, email verified, login success/failure, lockout, password reset, sign-out, email delivery failures, token reuse) | `AuditMiddleware` / `IAuditLogger` → `usp_AuditLogs_Insert` |

```sql
USE Orvanta;
-- Newest errors, in India time
SELECT TOP 50 AppLogId, LoggedAtUtc AT TIME ZONE 'UTC' AT TIME ZONE 'India Standard Time' AS LoggedAtIST,
       ServiceName, LogLevel, Message, ExceptionMessage
FROM AppLogs ORDER BY AppLogId DESC;

-- A user reported "Something went wrong. Reference: 0HN7..."
EXEC usp_AppLogs_Search @TraceId = N'0HN7...';
SELECT * FROM ufn_AppLogs_ErrorSummary(DATEADD(DAY, -1, SYSUTCDATETIME()));
EXEC usp_AuditLogs_Search @Action = N'Auth.LoginFailed', @Top = 50;
```

Optional settings per service: `DatabaseLogging:Enabled` (default true), `DatabaseLogging:MinimumLevel` (default `Warning`).

## Scheduled housekeeping

| Procedure | Suggested schedule |
|---|---|
| `usp_Logs_Purge` (90 days app logs, 365 days audit) | daily |
| `usp_Auth_PurgeExpired` (refresh tokens rotate every few minutes) | daily |
| `usp_PendingAuthorizations_PurgeExpired` | daily |
| `usp_JobPostings_PurgeStale` | weekly |

## Known model limitation

`OAuthClientRegistrations` has a primary key on `(Provider nvarchar(40), RedirectUri nvarchar(500))`, which can
reach 1,080 bytes; SQL Server warns because clustered keys are limited to 900. Real values are far shorter, so it works,
but shortening `RedirectUri` in the FoodService model (e.g. to 400) would remove the warning.
