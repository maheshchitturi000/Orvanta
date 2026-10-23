SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO
-- Support: a user's recent orders across providers.
-- Example: EXEC dbo.usp_FoodOrders_GetByUser @UserId = '...', @Top = 20;
CREATE OR ALTER PROCEDURE dbo.usp_FoodOrders_GetByUser
    @UserId UNIQUEIDENTIFIER,
    @Top    INT = 50
AS
BEGIN
    SET NOCOUNT ON;

    SELECT TOP (@Top)
           Id, Provider, ProviderOrderId, RestaurantName, ItemName, Quantity, TotalAmount,
           PaymentMethod, Status, CreatedAtUtc
    FROM dbo.FoodOrders
    WHERE UserId = @UserId
    ORDER BY CreatedAtUtc DESC;
END
GO
