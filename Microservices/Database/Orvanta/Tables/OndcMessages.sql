-- ONDC: every protocol message Orvanta sends (Out) or receives (In), with whether its signature verified.
-- ONDC requires participants to keep transaction logs for audits and dispute resolution.
-- Owned by Orvanta.FoodService (Ondc module).
IF OBJECT_ID(N'dbo.OndcMessages', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.OndcMessages
    (
        OndcMessageId  BIGINT IDENTITY(1, 1) NOT NULL,
        Direction      NVARCHAR(3)      NOT NULL,
        Action         NVARCHAR(30)     NOT NULL,
        TransactionId  NVARCHAR(100)    NULL,
        MessageId      NVARCHAR(100)    NULL,
        Counterparty   NVARCHAR(500)    NULL,
        PayloadJson    NVARCHAR(MAX)    NOT NULL,
        SignatureValid BIT              NULL,
        HttpStatus     INT              NULL,
        Error          NVARCHAR(500)    NULL,
        CreatedAtUtc   DATETIME2(3)     NOT NULL CONSTRAINT DF_OndcMessages_CreatedAtUtc DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT PK_OndcMessages PRIMARY KEY CLUSTERED (OndcMessageId),
        CONSTRAINT CK_OndcMessages_Direction CHECK (Direction IN (N'In', N'Out'))
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_OndcMessages_TransactionId' AND object_id = OBJECT_ID(N'dbo.OndcMessages'))
    CREATE INDEX IX_OndcMessages_TransactionId ON dbo.OndcMessages (TransactionId, CreatedAtUtc);
GO
