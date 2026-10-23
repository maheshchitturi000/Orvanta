SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO
-- ConversationRepository.CreateConversationAsync
CREATE OR ALTER PROCEDURE dbo.usp_Conversations_Create
    @ConversationId UNIQUEIDENTIFIER,
    @UserId         UNIQUEIDENTIFIER,
    @Title          NVARCHAR(200)
AS
BEGIN
    SET NOCOUNT ON;

    INSERT INTO dbo.Conversations (ConversationId, UserId, Title, CreatedAt, UpdatedAt)
    OUTPUT INSERTED.ConversationId
    VALUES (@ConversationId, @UserId, @Title, SYSUTCDATETIME(), SYSUTCDATETIME());
END
GO
