-- ONDC: an order from quote to delivery. One item per order, cash on delivery (ONDC "ON-FULFILLMENT").
-- State follows the buyer flow: Quoting → Quoted → Initializing → Initialized → Confirming → Confirmed,
-- then the seller's order states (Accepted, In-progress, Completed, Cancelled) from on_status; Failed on errors.
-- Owned by Orvanta.FoodService (Ondc module).
IF OBJECT_ID(N'dbo.OndcOrders', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.OndcOrders
    (
        OrderId           UNIQUEIDENTIFIER NOT NULL,
        UserId            UNIQUEIDENTIFIER NOT NULL,
        TransactionId     UNIQUEIDENTIFIER NOT NULL,
        BppId             NVARCHAR(200)    NOT NULL,
        BppUri            NVARCHAR(500)    NOT NULL,
        ProviderId        NVARCHAR(200)    NOT NULL,
        ProviderName      NVARCHAR(200)    NOT NULL,
        LocationId        NVARCHAR(200)    NULL,
        ItemId            NVARCHAR(200)    NOT NULL,
        ItemName          NVARCHAR(300)    NOT NULL,
        City              NVARCHAR(20)     NOT NULL CONSTRAINT DF_OndcOrders_City DEFAULT (N'std:080'),
        Quantity          INT              NOT NULL,
        State             NVARCHAR(30)     NOT NULL,
        QuoteTotal        DECIMAL(10, 2)   NULL,
        Currency          NVARCHAR(10)     NOT NULL CONSTRAINT DF_OndcOrders_Currency DEFAULT (N'INR'),
        QuoteBreakupJson  NVARCHAR(MAX)    NULL,
        DeliveryJson      NVARCHAR(MAX)    NULL,
        BppOrderId        NVARCHAR(200)    NULL,
        Error             NVARCHAR(500)    NULL,
        CreatedAtUtc      DATETIME2(3)     NOT NULL CONSTRAINT DF_OndcOrders_CreatedAtUtc DEFAULT (SYSUTCDATETIME()),
        UpdatedAtUtc      DATETIME2(3)     NOT NULL CONSTRAINT DF_OndcOrders_UpdatedAtUtc DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT PK_OndcOrders PRIMARY KEY CLUSTERED (OrderId),
        CONSTRAINT CK_OndcOrders_Quantity CHECK (Quantity BETWEEN 1 AND 20)
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_OndcOrders_UserId_CreatedAtUtc' AND object_id = OBJECT_ID(N'dbo.OndcOrders'))
    CREATE INDEX IX_OndcOrders_UserId_CreatedAtUtc ON dbo.OndcOrders (UserId, CreatedAtUtc DESC);
GO

-- Callbacks (on_select, on_init, on_confirm, on_status) find the order by its ONDC transaction.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_OndcOrders_TransactionId' AND object_id = OBJECT_ID(N'dbo.OndcOrders'))
    CREATE UNIQUE INDEX UX_OndcOrders_TransactionId ON dbo.OndcOrders (TransactionId);
GO

-- 2026-10: orders keep the ONDC city code of their search (every later message must use the same city).
IF COL_LENGTH(N'dbo.OndcOrders', N'City') IS NULL
    ALTER TABLE dbo.OndcOrders ADD City NVARCHAR(20) NOT NULL CONSTRAINT DF_OndcOrders_City DEFAULT (N'std:080');
GO
