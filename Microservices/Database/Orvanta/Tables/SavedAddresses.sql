-- Owned by Orvanta.FoodService (EF Core model). Keep in sync when the model changes.
IF OBJECT_ID(N'dbo.SavedAddresses', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[SavedAddresses] (
        [Id] uniqueidentifier NOT NULL,
        [UserId] uniqueidentifier NOT NULL,
        [Provider] nvarchar(40) NOT NULL,
        [ExternalId] nvarchar(100) NOT NULL,
        [Label] nvarchar(100) NULL,
        [AddressLine] nvarchar(500) NULL,
        [PhoneNumber] nvarchar(20) NULL,
        [IsDefault] bit NOT NULL,
        [SyncedAtUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_SavedAddresses] PRIMARY KEY ([Id])
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_SavedAddresses_UserId_Provider_ExternalId' AND object_id = OBJECT_ID(N'dbo.SavedAddresses'))
    CREATE UNIQUE INDEX [IX_SavedAddresses_UserId_Provider_ExternalId] ON [dbo].[SavedAddresses] ([UserId], [Provider], [ExternalId]);
GO
