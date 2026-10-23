-- Owned by Orvanta.FoodService (EF Core model). Keep in sync when the model changes.
IF OBJECT_ID(N'dbo.FoodOrders', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[FoodOrders] (
        [Id] uniqueidentifier NOT NULL,
        [UserId] uniqueidentifier NOT NULL,
        [Provider] nvarchar(40) NOT NULL,
        [ProviderOrderId] nvarchar(100) NULL,
        [RestaurantId] nvarchar(100) NOT NULL,
        [RestaurantName] nvarchar(200) NOT NULL,
        [MenuItemId] nvarchar(100) NOT NULL,
        [ItemName] nvarchar(200) NOT NULL,
        [Quantity] int NOT NULL,
        [TotalAmount] decimal(10,2) NOT NULL,
        [PaymentMethod] nvarchar(30) NOT NULL,
        [AddressId] nvarchar(100) NOT NULL,
        [Status] nvarchar(30) NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_FoodOrders] PRIMARY KEY ([Id])
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_FoodOrders_UserId_CreatedAtUtc' AND object_id = OBJECT_ID(N'dbo.FoodOrders'))
    CREATE INDEX [IX_FoodOrders_UserId_CreatedAtUtc] ON [dbo].[FoodOrders] ([UserId], [CreatedAtUtc]);
GO
