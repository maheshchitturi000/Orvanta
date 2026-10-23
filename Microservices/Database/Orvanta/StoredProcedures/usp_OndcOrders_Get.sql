SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO
-- OndcRepository.GetOrderAsync: one order. @UserId NULL is for server-side callbacks (no user context);
-- user-facing calls always pass it so people only see their own orders.
CREATE OR ALTER PROCEDURE dbo.usp_OndcOrders_Get
    @OrderId       UNIQUEIDENTIFIER = NULL,
    @TransactionId UNIQUEIDENTIFIER = NULL,
    @UserId        UNIQUEIDENTIFIER = NULL
AS
BEGIN
    SET NOCOUNT ON;

    SELECT OrderId, UserId, TransactionId, BppId, BppUri, ProviderId, ProviderName, LocationId, ItemId, ItemName,
           City, Quantity, State, QuoteTotal, Currency, QuoteBreakupJson, DeliveryJson, BppOrderId, Error,
           CreatedAtUtc, UpdatedAtUtc
    FROM dbo.OndcOrders
    WHERE (@OrderId IS NULL OR OrderId = @OrderId)
      AND (@TransactionId IS NULL OR TransactionId = @TransactionId)
      AND (@UserId IS NULL OR UserId = @UserId)
      AND (@OrderId IS NOT NULL OR @TransactionId IS NOT NULL);
END
GO
