-- ONDC: dishes returned by sellers (BPPs) for a search, one row per seller item (from on_search callbacks).
-- Owned by Orvanta.FoodService (Ondc module).
IF OBJECT_ID(N'dbo.OndcCatalogItems', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.OndcCatalogItems
    (
        CatalogItemId BIGINT IDENTITY(1, 1) NOT NULL,
        TransactionId UNIQUEIDENTIFIER NOT NULL,
        BppId         NVARCHAR(200)    NOT NULL,
        BppUri        NVARCHAR(500)    NOT NULL,
        ProviderId    NVARCHAR(200)    NOT NULL,
        ProviderName  NVARCHAR(200)    NOT NULL,
        LocationId    NVARCHAR(200)    NULL,
        ItemId        NVARCHAR(200)    NOT NULL,
        ItemName      NVARCHAR(300)    NOT NULL,
        Price         DECIMAL(10, 2)   NULL,
        Currency      NVARCHAR(10)     NOT NULL CONSTRAINT DF_OndcCatalogItems_Currency DEFAULT (N'INR'),
        IsVeg         BIT              NULL,
        ImageUrl      NVARCHAR(1000)   NULL,
        ReceivedAtUtc DATETIME2(3)     NOT NULL CONSTRAINT DF_OndcCatalogItems_ReceivedAtUtc DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT PK_OndcCatalogItems PRIMARY KEY CLUSTERED (CatalogItemId),
        CONSTRAINT FK_OndcCatalogItems_OndcSearches FOREIGN KEY (TransactionId)
            REFERENCES dbo.OndcSearches (TransactionId) ON DELETE CASCADE
    );
END
GO

-- One row per seller item per search; a repeated on_search never duplicates it.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_OndcCatalogItems_Item' AND object_id = OBJECT_ID(N'dbo.OndcCatalogItems'))
    CREATE UNIQUE INDEX UX_OndcCatalogItems_Item ON dbo.OndcCatalogItems (TransactionId, BppId, ProviderId, ItemId);
GO
