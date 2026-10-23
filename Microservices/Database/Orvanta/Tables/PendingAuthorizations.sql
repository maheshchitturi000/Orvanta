-- Owned by Orvanta.FoodService (EF Core model). Keep in sync when the model changes.
IF OBJECT_ID(N'dbo.PendingAuthorizations', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[PendingAuthorizations] (
        [StateHash] nvarchar(64) NOT NULL,
        [UserId] uniqueidentifier NOT NULL,
        [Provider] nvarchar(40) NOT NULL,
        [CodeVerifierEncrypted] nvarchar(max) NOT NULL,
        [RedirectUri] nvarchar(500) NOT NULL,
        [ExpiresAtUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_PendingAuthorizations] PRIMARY KEY ([StateHash])
    );
END
GO
