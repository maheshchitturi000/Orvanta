-- Owned by Orvanta.Jobs.Core (EF Core model). Keep in sync when the model changes.
IF OBJECT_ID(N'dbo.JobApplications', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[JobApplications] (
        [Id] uniqueidentifier NOT NULL,
        [UserId] uniqueidentifier NOT NULL,
        [JobMatchId] uniqueidentifier NOT NULL,
        [JobPostingId] uniqueidentifier NOT NULL,
        [Status] int NOT NULL,
        [Channel] nvarchar(20) NOT NULL,
        [CoverLetter] nvarchar(max) NOT NULL,
        [AnswersJson] nvarchar(max) NOT NULL,
        [Error] nvarchar(1000) NOT NULL,
        [Attempts] int NOT NULL,
        [ApprovedAtUtc] datetime2 NOT NULL,
        [PreparedAtUtc] datetime2 NULL,
        [SubmittedAtUtc] datetime2 NULL,
        [UpdatedAtUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_JobApplications] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_JobApplications_JobPostings_JobPostingId] FOREIGN KEY ([JobPostingId]) REFERENCES [dbo].[JobPostings] ([Id])
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_JobApplications_JobMatchId' AND object_id = OBJECT_ID(N'dbo.JobApplications'))
    CREATE UNIQUE INDEX [IX_JobApplications_JobMatchId] ON [dbo].[JobApplications] ([JobMatchId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_JobApplications_JobPostingId' AND object_id = OBJECT_ID(N'dbo.JobApplications'))
    CREATE INDEX [IX_JobApplications_JobPostingId] ON [dbo].[JobApplications] ([JobPostingId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_JobApplications_UserId_Status' AND object_id = OBJECT_ID(N'dbo.JobApplications'))
    CREATE INDEX [IX_JobApplications_UserId_Status] ON [dbo].[JobApplications] ([UserId], [Status]);
GO
