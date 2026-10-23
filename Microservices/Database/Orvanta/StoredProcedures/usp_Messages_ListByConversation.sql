SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO
-- ConversationRepository.GetMessagesAsync: oldest first.
CREATE OR ALTER PROCEDURE dbo.usp_Messages_ListByConversation
    @ConversationId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;

    SELECT MessageId, ConversationId, Role, Content, CreatedAt
    FROM dbo.Messages
    WHERE ConversationId = @ConversationId
    ORDER BY MessageId ASC;
END
GO
