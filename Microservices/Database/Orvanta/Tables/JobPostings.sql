-- Owned by Orvanta.Jobs.Core (EF Core model). Keep in sync when the model changes.
IF OBJECT_ID(N'dbo.JobPostings', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[JobPostings] (
        [Id] uniqueidentifier NOT NULL,
        [Source] nvarchar(30) NOT NULL,
        [ExternalId] nvarchar(100) NOT NULL,
        [Company] nvarchar(150) NOT NULL,
        [Title] nvarchar(300) NOT NULL,
        [Location] nvarchar(300) NOT NULL,
        [IsRemote] bit NOT NULL,
        [Url] nvarchar(500) NOT NULL,
        [ApplyUrl] nvarchar(500) NOT NULL,
        [Description] nvarchar(max) NOT NULL,
        [PostedAtUtc] datetime2 NULL,
        [FetchedAtUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_JobPostings] PRIMARY KEY ([Id])
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_JobPostings_Source_ExternalId' AND object_id = OBJECT_ID(N'dbo.JobPostings'))
    CREATE UNIQUE INDEX [IX_JobPostings_Source_ExternalId] ON [dbo].[JobPostings] ([Source], [ExternalId]);
GO

-- 2026-10: "View" used Google's job listing page (job_google_link). Url is now the real job page; fix rows saved
-- before that. Safe to run on every deploy: only rows still pointing at Google with a real link available change.
UPDATE [dbo].[JobPostings]
SET [Url] = [ApplyUrl]
WHERE [Url] LIKE N'https://www.google.com/search%' AND [ApplyUrl] <> N'';
GO
