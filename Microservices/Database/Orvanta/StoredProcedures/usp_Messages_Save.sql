SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO
-- ConversationRepository.SaveMessageAsync: stores the message and bumps the conversation in one transaction.
CREATE OR ALTER PROCEDURE dbo.usp_Messages_Save
    @ConversationId UNIQUEIDENTIFIER,
    @Role           NVARCHAR(20),
    @Content        NVARCHAR(MAX)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRANSACTION;

    INSERT INTO dbo.Messages (ConversationId, Role, Content, CreatedAt)
    VALUES (@ConversationId, @Role, @Content, SYSUTCDATETIME());

    UPDATE dbo.Conversations
    SET UpdatedAt = SYSUTCDATETIME()
    WHERE ConversationId = @ConversationId;

    COMMIT TRANSACTION;
END
GO
