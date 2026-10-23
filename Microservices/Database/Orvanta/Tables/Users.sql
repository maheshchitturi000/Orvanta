-- Owned by Orvanta.AuthService (EF Core model). Keep in sync when the model changes.
IF OBJECT_ID(N'dbo.Users', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[Users] (
        [Id] uniqueidentifier NOT NULL,
        [FullName] nvarchar(100) NOT NULL,
        [Email] nvarchar(254) NOT NULL,
        [PhoneNumber] nvarchar(15) NOT NULL,
        [PasswordHash] nvarchar(200) NOT NULL,
        [Role] nvarchar(20) NOT NULL,
        [IsEmailVerified] bit NOT NULL,
        [EmailVerifiedAtUtc] datetime2 NULL,
        [TermsVersion] nvarchar(20) NOT NULL,
        [TermsAcceptedAtUtc] datetime2 NOT NULL,
        [FailedLoginCount] int NOT NULL,
        [LockoutEndUtc] datetime2 NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [LastLoginAtUtc] datetime2 NULL,
        CONSTRAINT [PK_Users] PRIMARY KEY ([Id])
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Users_Email' AND object_id = OBJECT_ID(N'dbo.Users'))
    CREATE UNIQUE INDEX [IX_Users_Email] ON [dbo].[Users] ([Email]);
GO
