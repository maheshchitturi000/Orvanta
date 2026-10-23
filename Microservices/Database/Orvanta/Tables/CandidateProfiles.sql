-- Owned by Orvanta.Jobs.Core (EF Core model). Keep in sync when the model changes.
IF OBJECT_ID(N'dbo.CandidateProfiles', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[CandidateProfiles] (
        [UserId] uniqueidentifier NOT NULL,
        [FullName] nvarchar(100) NOT NULL,
        [Email] nvarchar(254) NOT NULL,
        [Phone] nvarchar(30) NOT NULL,
        [Headline] nvarchar(200) NOT NULL,
        [CurrentTitle] nvarchar(150) NOT NULL,
        [Location] nvarchar(150) NOT NULL,
        [TotalYearsExperience] decimal(4,1) NOT NULL,
        [Skills] nvarchar(max) NOT NULL,
        [Summary] nvarchar(2000) NOT NULL,
        [LinkedInUrl] nvarchar(300) NOT NULL,
        [PortfolioUrl] nvarchar(300) NOT NULL,
        [CvFileName] nvarchar(200) NOT NULL,
        [CvContentType] nvarchar(100) NOT NULL,
        [CvFile] varbinary(max) NOT NULL,
        [CvText] nvarchar(max) NOT NULL,
        [UpdatedAtUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_CandidateProfiles] PRIMARY KEY ([UserId])
    );
END
GO
