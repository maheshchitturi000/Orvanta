SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO
-- OndcRepository.GetCatalogItemAsync: the dish a user picked, checked against their own search.
CREATE OR ALTER PROCEDURE dbo.usp_OndcCatalogItems_Get
    @CatalogItemId BIGINT,
    @UserId        UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;

    SELECT c.CatalogItemId, c.TransactionId, c.BppId, c.BppUri, c.ProviderId, c.ProviderName, c.LocationId,
           c.ItemId, c.ItemName, c.Price, c.Currency, c.IsVeg, c.ImageUrl, c.ReceivedAtUtc, s.City
    FROM dbo.OndcCatalogItems AS c
    JOIN dbo.OndcSearches AS s ON s.TransactionId = c.TransactionId
    WHERE c.CatalogItemId = @CatalogItemId
      AND s.UserId = @UserId;
END
GO
