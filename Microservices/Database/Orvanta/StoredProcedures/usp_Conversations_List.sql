SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO
-- ConversationRepository.GetConversationsAsync: the user's conversations, most recently active first.
CREATE OR ALTER PROCEDURE dbo.usp_Conversations_List
    @UserId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;

    SELECT ConversationId, Title, CreatedAt, UpdatedAt
    FROM dbo.Conversations
    WHERE UserId = @UserId
    ORDER BY UpdatedAt DESC;
END
GO
