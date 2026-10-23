SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO
-- Per-conversation message counts and last activity (reporting / cleanup of empty conversations).
-- Example: SELECT * FROM dbo.ufn_Conversations_Activity() WHERE MessageCount = 0;
CREATE OR ALTER FUNCTION dbo.ufn_Conversations_Activity ()
RETURNS TABLE
AS
RETURN
    SELECT c.ConversationId,
           c.Title,
           c.CreatedAt,
           c.UpdatedAt,
           COUNT(m.MessageId) AS MessageCount,
           MAX(m.CreatedAt)   AS LastMessageAt
    FROM dbo.Conversations AS c
    LEFT JOIN dbo.Messages AS m ON m.ConversationId = c.ConversationId
    GROUP BY c.ConversationId, c.Title, c.CreatedAt, c.UpdatedAt;
GO
