-- ONDC (Open Network for Digital Commerce): one row per food search a user sends to the network.
-- TransactionId is the ONDC transaction_id; sellers answer asynchronously (on_search) into OndcCatalogItems.
-- Owned by Orvanta.FoodService (Ondc module, Dapper + procedures).
IF OBJECT_ID(N'dbo.OndcSearches', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.OndcSearches
    (
        TransactionId UNIQUEIDENTIFIER NOT NULL,
        UserId        UNIQUEIDENTIFIER NOT NULL,
        Query         NVARCHAR(200)    NOT NULL,
        City          NVARCHAR(20)     NOT NULL,
        Status        NVARCHAR(20)     NOT NULL CONSTRAINT DF_OndcSearches_Status DEFAULT (N'Sent'),
        Error         NVARCHAR(500)    NULL,
        CreatedAtUtc  DATETIME2(3)     NOT NULL CONSTRAINT DF_OndcSearches_CreatedAtUtc DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT PK_OndcSearches PRIMARY KEY CLUSTERED (TransactionId),
        CONSTRAINT CK_OndcSearches_Status CHECK (Status IN (N'Sent', N'Failed'))
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_OndcSearches_UserId_CreatedAtUtc' AND object_id = OBJECT_ID(N'dbo.OndcSearches'))
    CREATE INDEX IX_OndcSearches_UserId_CreatedAtUtc ON dbo.OndcSearches (UserId, CreatedAtUtc DESC);
GO
