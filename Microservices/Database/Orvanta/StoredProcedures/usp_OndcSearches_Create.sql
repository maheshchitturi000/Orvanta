SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO
-- OndcRepository.CreateSearchAsync: a search is recorded before it is sent, so early callbacks find it.
CREATE OR ALTER PROCEDURE dbo.usp_OndcSearches_Create
    @TransactionId UNIQUEIDENTIFIER,
    @UserId        UNIQUEIDENTIFIER,
    @Query         NVARCHAR(200),
    @City          NVARCHAR(20)
AS
BEGIN
    SET NOCOUNT ON;

    INSERT INTO dbo.OndcSearches (TransactionId, UserId, Query, City)
    VALUES (@TransactionId, @UserId, @Query, @City);
END
GO
