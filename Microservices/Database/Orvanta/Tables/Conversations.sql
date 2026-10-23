-- Owned by Orvanta.ConversationService (Dapper). Chat conversations shown in the sidebar history.
IF OBJECT_ID(N'dbo.Conversations', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Conversations
    (
        ConversationId UNIQUEIDENTIFIER NOT NULL,
        UserId         UNIQUEIDENTIFIER NULL,
        Title          NVARCHAR(200)    NOT NULL,
        CreatedAt      DATETIME2        NOT NULL CONSTRAINT DF_Conversations_CreatedAt DEFAULT (SYSUTCDATETIME()),
        UpdatedAt      DATETIME2        NOT NULL CONSTRAINT DF_Conversations_UpdatedAt DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT PK_Conversations PRIMARY KEY CLUSTERED (ConversationId)
    );
END
GO

-- 2026-10: conversations belong to the signed-in user. Rows created before this have no owner and are
-- shown to nobody (they were saved anonymously).
IF COL_LENGTH(N'dbo.Conversations', N'UserId') IS NULL
    ALTER TABLE dbo.Conversations ADD UserId UNIQUEIDENTIFIER NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Conversations_UserId_UpdatedAt' AND object_id = OBJECT_ID(N'dbo.Conversations'))
    CREATE INDEX IX_Conversations_UserId_UpdatedAt ON dbo.Conversations (UserId, UpdatedAt DESC);
GO
