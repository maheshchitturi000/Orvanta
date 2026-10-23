SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO
-- OndcRepository.LogMessageAsync: audit log of every ONDC message sent or received.
CREATE OR ALTER PROCEDURE dbo.usp_OndcMessages_Insert
    @Direction      NVARCHAR(3),
    @Action         NVARCHAR(30),
    @TransactionId  NVARCHAR(100) = NULL,
    @MessageId      NVARCHAR(100) = NULL,
    @Counterparty   NVARCHAR(500) = NULL,
    @PayloadJson    NVARCHAR(MAX),
    @SignatureValid BIT           = NULL,
    @HttpStatus     INT           = NULL,
    @Error          NVARCHAR(MAX) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    INSERT INTO dbo.OndcMessages
        (Direction, Action, TransactionId, MessageId, Counterparty, PayloadJson, SignatureValid, HttpStatus, Error)
    VALUES
        (@Direction, LEFT(@Action, 30), LEFT(@TransactionId, 100), LEFT(@MessageId, 100), LEFT(@Counterparty, 500),
         @PayloadJson, @SignatureValid, @HttpStatus, LEFT(@Error, 500));
END
GO
