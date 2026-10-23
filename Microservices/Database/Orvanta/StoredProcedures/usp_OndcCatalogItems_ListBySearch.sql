SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO
-- OndcRepository.ListCatalogAsync: results of one of the user's searches (only the owner sees them).
CREATE OR ALTER PROCEDURE dbo.usp_OndcCatalogItems_ListBySearch
    @TransactionId UNIQUEIDENTIFIER,
    @UserId        UNIQUEIDENTIFIER,
    @Top           INT = 50
AS
BEGIN
    SET NOCOUNT ON;

    SELECT TOP (@Top)
           c.CatalogItemId, c.TransactionId, c.BppId, c.BppUri, c.ProviderId, c.ProviderName, c.LocationId,
           c.ItemId, c.ItemName, c.Price, c.Currency, c.IsVeg, c.ImageUrl, c.ReceivedAtUtc, s.City
    FROM dbo.OndcCatalogItems AS c
    JOIN dbo.OndcSearches AS s ON s.TransactionId = c.TransactionId
    WHERE c.TransactionId = @TransactionId
      AND s.UserId = @UserId
    ORDER BY CASE WHEN c.Price IS NULL THEN 1 ELSE 0 END, c.Price, c.ItemName;
END
GO
