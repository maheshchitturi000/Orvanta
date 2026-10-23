-- Owned by Orvanta.ConversationService (Dapper). Chat conversations shown in the sidebar history.
IF OBJECT_ID(N'dbo.Conversations', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Conversations
    (
        ConversationId UNIQUEIDENTIFIER NOT NULL,
        Title          NVARCHAR(200)    NOT NULL,
        CreatedAt      DATETIME2        NOT NULL CONSTRAINT DF_Conversations_CreatedAt DEFAULT (SYSUTCDATETIME()),
        UpdatedAt      DATETIME2        NOT NULL CONSTRAINT DF_Conversations_UpdatedAt DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT PK_Conversations PRIMARY KEY CLUSTERED (ConversationId)
    );
END
GO
