SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO
-- OndcRepository.UpdateOrderAsync: moves an order to a new state. Optional values left NULL keep what is
-- stored, so each step only sends what it learned (quote, delivery details, the seller's order id, an error).
CREATE OR ALTER PROCEDURE dbo.usp_OndcOrders_Update
    @OrderId          UNIQUEIDENTIFIER,
    @State            NVARCHAR(30),
    @QuoteTotal       DECIMAL(10, 2) = NULL,
    @Currency         NVARCHAR(10)   = NULL,
    @QuoteBreakupJson NVARCHAR(MAX)  = NULL,
    @DeliveryJson     NVARCHAR(MAX)  = NULL,
    @BppOrderId       NVARCHAR(200)  = NULL,
    @Error            NVARCHAR(MAX)  = NULL
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.OndcOrders
    SET State            = @State,
        QuoteTotal       = ISNULL(@QuoteTotal, QuoteTotal),
        Currency         = ISNULL(@Currency, Currency),
        QuoteBreakupJson = ISNULL(@QuoteBreakupJson, QuoteBreakupJson),
        DeliveryJson     = ISNULL(@DeliveryJson, DeliveryJson),
        BppOrderId       = ISNULL(@BppOrderId, BppOrderId),
        Error            = CASE WHEN @Error IS NULL THEN Error ELSE LEFT(@Error, 500) END,
        UpdatedAtUtc     = SYSUTCDATETIME()
    WHERE OrderId = @OrderId;
END
GO
