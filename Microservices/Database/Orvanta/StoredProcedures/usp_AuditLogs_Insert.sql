SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO
-- Called by Orvanta.Shared DatabaseLogWriter for every audit event.
CREATE OR ALTER PROCEDURE dbo.usp_AuditLogs_Insert
    @OccurredAtUtc DATETIME2(3),
    @ServiceName   NVARCHAR(60),
    @Action        NVARCHAR(100),
    @Outcome       NVARCHAR(10),
    @UserId        UNIQUEIDENTIFIER = NULL,
    @UserEmail     NVARCHAR(254)    = NULL,
    @EntityType    NVARCHAR(100)    = NULL,
    @EntityId      NVARCHAR(100)    = NULL,
    @HttpMethod    NVARCHAR(10)     = NULL,
    @RequestPath   NVARCHAR(MAX)    = NULL,
    @StatusCode    INT              = NULL,
    @DurationMs    INT              = NULL,
    @TraceId       NVARCHAR(64)     = NULL,
    @ClientIp      NVARCHAR(64)     = NULL,
    @UserAgent     NVARCHAR(MAX)    = NULL,
    @Details       NVARCHAR(MAX)    = NULL
AS
BEGIN
    SET NOCOUNT ON;

    INSERT INTO dbo.AuditLogs
        (OccurredAtUtc, ServiceName, Action, Outcome, UserId, UserEmail, EntityType, EntityId,
         HttpMethod, RequestPath, StatusCode, DurationMs, TraceId, ClientIp, UserAgent, Details)
    VALUES
        (ISNULL(@OccurredAtUtc, SYSUTCDATETIME()), @ServiceName, LEFT(@Action, 100), @Outcome, @UserId, @UserEmail,
         @EntityType, @EntityId, @HttpMethod, LEFT(@RequestPath, 500), @StatusCode, @DurationMs, @TraceId,
         @ClientIp, LEFT(@UserAgent, 512), LEFT(@Details, 2000));
END
GO
