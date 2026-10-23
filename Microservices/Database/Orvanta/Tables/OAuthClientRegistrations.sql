-- Owned by Orvanta.FoodService (EF Core model). Keep in sync when the model changes.
IF OBJECT_ID(N'dbo.OAuthClientRegistrations', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[OAuthClientRegistrations] (
        [Provider] nvarchar(40) NOT NULL,
        [RedirectUri] nvarchar(500) NOT NULL,
        [ClientId] nvarchar(200) NOT NULL,
        [ClientSecretEncrypted] nvarchar(max) NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_OAuthClientRegistrations] PRIMARY KEY ([Provider], [RedirectUri])
    );
END
GO
