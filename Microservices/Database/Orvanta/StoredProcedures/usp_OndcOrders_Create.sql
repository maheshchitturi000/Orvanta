SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO
-- OndcRepository.CreateOrderAsync: starts an order in state "Quoting" (a select is about to be sent).
CREATE OR ALTER PROCEDURE dbo.usp_OndcOrders_Create
    @OrderId       UNIQUEIDENTIFIER,
    @UserId        UNIQUEIDENTIFIER,
    @TransactionId UNIQUEIDENTIFIER,
    @BppId         NVARCHAR(200),
    @BppUri        NVARCHAR(500),
    @ProviderId    NVARCHAR(200),
    @ProviderName  NVARCHAR(200),
    @LocationId    NVARCHAR(200) = NULL,
    @ItemId        NVARCHAR(200),
    @ItemName      NVARCHAR(300),
    @City          NVARCHAR(20),
    @Quantity      INT,
    @Currency      NVARCHAR(10)  = N'INR'
AS
BEGIN
    SET NOCOUNT ON;

    INSERT INTO dbo.OndcOrders
        (OrderId, UserId, TransactionId, BppId, BppUri, ProviderId, ProviderName, LocationId, ItemId, ItemName,
         City, Quantity, State, Currency)
    VALUES
        (@OrderId, @UserId, @TransactionId, @BppId, @BppUri, @ProviderId, @ProviderName, @LocationId, @ItemId, @ItemName,
         @City, @Quantity, N'Quoting', @Currency);
END
GO
