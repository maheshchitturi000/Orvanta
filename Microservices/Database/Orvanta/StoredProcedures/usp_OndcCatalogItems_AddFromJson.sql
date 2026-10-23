SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO
-- OndcRepository.AddCatalogItemsAsync: stores the dishes from one seller's on_search in a single call.
-- @ItemsJson is an array of { bppId, bppUri, providerId, providerName, locationId, itemId, itemName, price,
-- currency, isVeg, imageUrl }. Items already stored for this search are skipped (on_search can repeat).
-- Returns the number of new rows.
CREATE OR ALTER PROCEDURE dbo.usp_OndcCatalogItems_AddFromJson
    @TransactionId UNIQUEIDENTIFIER,
    @ItemsJson     NVARCHAR(MAX)
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM dbo.OndcSearches WHERE TransactionId = @TransactionId)
    BEGIN
        SELECT 0 AS Added; -- unknown or expired search: ignore
        RETURN;
    END

    INSERT INTO dbo.OndcCatalogItems
        (TransactionId, BppId, BppUri, ProviderId, ProviderName, LocationId, ItemId, ItemName, Price, Currency, IsVeg, ImageUrl)
    SELECT DISTINCT
        @TransactionId, j.BppId, j.BppUri, j.ProviderId, LEFT(j.ProviderName, 200), j.LocationId, j.ItemId,
        LEFT(j.ItemName, 300), j.Price, ISNULL(j.Currency, N'INR'), j.IsVeg, LEFT(j.ImageUrl, 1000)
    FROM OPENJSON(@ItemsJson)
        WITH (
            BppId        NVARCHAR(200)  '$.bppId',
            BppUri       NVARCHAR(500)  '$.bppUri',
            ProviderId   NVARCHAR(200)  '$.providerId',
            ProviderName NVARCHAR(400)  '$.providerName',
            LocationId   NVARCHAR(200)  '$.locationId',
            ItemId       NVARCHAR(200)  '$.itemId',
            ItemName     NVARCHAR(600)  '$.itemName',
            Price        DECIMAL(10, 2) '$.price',
            Currency     NVARCHAR(10)   '$.currency',
            IsVeg        BIT            '$.isVeg',
            ImageUrl     NVARCHAR(2000) '$.imageUrl'
        ) AS j
    WHERE j.BppId IS NOT NULL AND j.ProviderId IS NOT NULL AND j.ItemId IS NOT NULL AND j.ItemName IS NOT NULL
      AND NOT EXISTS (
          SELECT 1 FROM dbo.OndcCatalogItems c
          WHERE c.TransactionId = @TransactionId AND c.BppId = j.BppId AND c.ProviderId = j.ProviderId AND c.ItemId = j.ItemId);

    SELECT @@ROWCOUNT AS Added;
END
GO
