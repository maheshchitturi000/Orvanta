-- Owned by Orvanta.AuthService (EF Core model). Keep in sync when the model changes.
IF OBJECT_ID(N'dbo.EmailOtps', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[EmailOtps] (
        [Id] uniqueidentifier NOT NULL,
        [UserId] uniqueidentifier NOT NULL,
        [CodeHash] nvarchar(64) NOT NULL,
        [Purpose] nvarchar(30) NOT NULL CONSTRAINT [DF_EmailOtps_Purpose] DEFAULT (N'EmailVerification'),
        [ExpiresAtUtc] datetime2 NOT NULL,
        [FailedAttempts] int NOT NULL,
        [ConsumedAtUtc] datetime2 NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_EmailOtps] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_EmailOtps_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [dbo].[Users] ([Id]) ON DELETE CASCADE
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_EmailOtps_UserId_CreatedAtUtc' AND object_id = OBJECT_ID(N'dbo.EmailOtps'))
    CREATE INDEX [IX_EmailOtps_UserId_CreatedAtUtc] ON [dbo].[EmailOtps] ([UserId], [CreatedAtUtc]);
GO

-- 2026-10: forgot-password. Codes are tied to a purpose so a reset code can't verify an email (and vice versa).
-- Existing rows were all email-verification codes, which the default records.
IF COL_LENGTH(N'dbo.EmailOtps', N'Purpose') IS NULL
    ALTER TABLE [dbo].[EmailOtps]
        ADD [Purpose] nvarchar(30) NOT NULL CONSTRAINT [DF_EmailOtps_Purpose] DEFAULT (N'EmailVerification');
GO
