-- Owned by Orvanta.Jobs.Core (EF Core model). Keep in sync when the model changes.
IF OBJECT_ID(N'dbo.JobSourceConnections', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[JobSourceConnections] (
        [UserId] uniqueidentifier NOT NULL,
        [Source] nvarchar(30) NOT NULL,
        [SecretEncrypted] nvarchar(1000) NOT NULL,
        [ConnectedAtUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_JobSourceConnections] PRIMARY KEY ([UserId], [Source])
    );
END
GO
