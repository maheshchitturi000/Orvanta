-- Owned by Orvanta.Jobs.Core (EF Core model). Keep in sync when the model changes.
IF OBJECT_ID(N'dbo.JobMatches', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[JobMatches] (
        [Id] uniqueidentifier NOT NULL,
        [UserId] uniqueidentifier NOT NULL,
        [JobPostingId] uniqueidentifier NOT NULL,
        [Score] int NOT NULL,
        [MatchedSkills] nvarchar(max) NOT NULL,
        [MissingSkills] nvarchar(max) NOT NULL,
        [Reasons] nvarchar(max) NOT NULL,
        [Status] int NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_JobMatches] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_JobMatches_JobPostings_JobPostingId] FOREIGN KEY ([JobPostingId]) REFERENCES [dbo].[JobPostings] ([Id]) ON DELETE CASCADE
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_JobMatches_JobPostingId' AND object_id = OBJECT_ID(N'dbo.JobMatches'))
    CREATE INDEX [IX_JobMatches_JobPostingId] ON [dbo].[JobMatches] ([JobPostingId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_JobMatches_UserId_JobPostingId' AND object_id = OBJECT_ID(N'dbo.JobMatches'))
    CREATE UNIQUE INDEX [IX_JobMatches_UserId_JobPostingId] ON [dbo].[JobMatches] ([UserId], [JobPostingId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_JobMatches_UserId_Status_Score' AND object_id = OBJECT_ID(N'dbo.JobMatches'))
    CREATE INDEX [IX_JobMatches_UserId_Status_Score] ON [dbo].[JobMatches] ([UserId], [Status], [Score]);
GO
