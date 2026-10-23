SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO
-- ConversationRepository.GetConversationsAsync: most recently active first.
CREATE OR ALTER PROCEDURE dbo.usp_Conversations_List
AS
BEGIN
    SET NOCOUNT ON;

    SELECT ConversationId, Title, CreatedAt, UpdatedAt
    FROM dbo.Conversations
    ORDER BY UpdatedAt DESC;
END
GO
