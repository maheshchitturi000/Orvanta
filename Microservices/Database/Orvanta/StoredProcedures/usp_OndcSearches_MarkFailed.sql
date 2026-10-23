SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO
-- OndcRepository.MarkSearchFailedAsync: the gateway refused the search (NACK, network or signing error).
CREATE OR ALTER PROCEDURE dbo.usp_OndcSearches_MarkFailed
    @TransactionId UNIQUEIDENTIFIER,
    @Error         NVARCHAR(MAX)
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.OndcSearches
    SET Status = N'Failed', Error = LEFT(@Error, 500)
    WHERE TransactionId = @TransactionId;
END
GO
