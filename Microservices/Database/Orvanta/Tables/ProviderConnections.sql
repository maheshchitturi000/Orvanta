-- Owned by Orvanta.FoodService (EF Core model). Keep in sync when the model changes.
IF OBJECT_ID(N'dbo.ProviderConnections', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[ProviderConnections] (
        [Id] uniqueidentifier NOT NULL,
        [UserId] uniqueidentifier NOT NULL,
        [Provider] nvarchar(40) NOT NULL,
        [AccessTokenEncrypted] nvarchar(max) NOT NULL,
        [RefreshTokenEncrypted] nvarchar(max) NULL,
        [AccessTokenExpiresAtUtc] datetime2 NOT NULL,
        [Scope] nvarchar(200) NULL,
        [ConnectedAtUtc] datetime2 NOT NULL,
        [UpdatedAtUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_ProviderConnections] PRIMARY KEY ([Id])
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ProviderConnections_UserId_Provider' AND object_id = OBJECT_ID(N'dbo.ProviderConnections'))
    CREATE UNIQUE INDEX [IX_ProviderConnections_UserId_Provider] ON [dbo].[ProviderConnections] ([UserId], [Provider]);
GO
