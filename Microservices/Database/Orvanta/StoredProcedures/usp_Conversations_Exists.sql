SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO
-- ConversationRepository.ConversationExistsAsync: true only when the conversation belongs to the user.
CREATE OR ALTER PROCEDURE dbo.usp_Conversations_Exists
    @ConversationId UNIQUEIDENTIFIER,
    @UserId         UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;

    SELECT CAST(CASE WHEN EXISTS (SELECT 1 FROM dbo.Conversations
                                  WHERE ConversationId = @ConversationId AND UserId = @UserId)
                     THEN 1 ELSE 0 END AS BIT);
END
GO
