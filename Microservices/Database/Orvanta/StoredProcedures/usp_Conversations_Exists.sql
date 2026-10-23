SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO
-- ConversationRepository.ConversationExistsAsync
CREATE OR ALTER PROCEDURE dbo.usp_Conversations_Exists
    @ConversationId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;

    SELECT CAST(CASE WHEN EXISTS (SELECT 1 FROM dbo.Conversations WHERE ConversationId = @ConversationId)
                     THEN 1 ELSE 0 END AS BIT);
END
GO
