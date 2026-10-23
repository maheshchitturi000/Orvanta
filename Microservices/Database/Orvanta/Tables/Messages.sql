-- Owned by Orvanta.ConversationService (Dapper). Messages of a conversation, oldest first by MessageId.
IF OBJECT_ID(N'dbo.Messages', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Messages
    (
        MessageId      BIGINT IDENTITY(1, 1) NOT NULL,
        ConversationId UNIQUEIDENTIFIER NOT NULL,
        Role           NVARCHAR(20)     NOT NULL,
        Content        NVARCHAR(MAX)    NOT NULL,
        CreatedAt      DATETIME2        NOT NULL CONSTRAINT DF_Messages_CreatedAt DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT PK_Messages PRIMARY KEY CLUSTERED (MessageId),
        CONSTRAINT FK_Messages_Conversations FOREIGN KEY (ConversationId)
            REFERENCES dbo.Conversations (ConversationId) ON DELETE CASCADE
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Messages_ConversationId_CreatedAt' AND object_id = OBJECT_ID(N'dbo.Messages'))
    CREATE INDEX IX_Messages_ConversationId_CreatedAt ON dbo.Messages (ConversationId, CreatedAt);
GO
