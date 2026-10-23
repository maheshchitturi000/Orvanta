SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO
-- OndcRepository.ListOrdersAsync: the user's ONDC orders, newest first.
CREATE OR ALTER PROCEDURE dbo.usp_OndcOrders_ListByUser
    @UserId UNIQUEIDENTIFIER,
    @Top    INT = 20
AS
BEGIN
    SET NOCOUNT ON;

    SELECT TOP (@Top)
           OrderId, UserId, TransactionId, BppId, BppUri, ProviderId, ProviderName, LocationId, ItemId, ItemName,
           City, Quantity, State, QuoteTotal, Currency, QuoteBreakupJson, DeliveryJson, BppOrderId, Error,
           CreatedAtUtc, UpdatedAtUtc
    FROM dbo.OndcOrders
    WHERE UserId = @UserId
    ORDER BY CreatedAtUtc DESC;
END
GO
