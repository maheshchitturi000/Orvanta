-- Owned by Orvanta.Jobs.Core (EF Core model). Keep in sync when the model changes.
IF OBJECT_ID(N'dbo.JobPreferences', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[JobPreferences] (
        [UserId] uniqueidentifier NOT NULL,
        [DesiredTitles] nvarchar(max) NOT NULL,
        [Locations] nvarchar(max) NOT NULL,
        [IncludeRemote] bit NOT NULL,
        [RemoteOnly] bit NOT NULL,
        [MinMatchScore] int NOT NULL,
        [MaxPostingAgeDays] int NOT NULL,
        [CompanyBoards] nvarchar(max) NOT NULL,
        [ExcludedCompanies] nvarchar(max) NOT NULL,
        [NoticePeriodDays] int NOT NULL,
        [WorkAuthorization] nvarchar(200) NOT NULL,
        [NeedsVisaSponsorship] bit NOT NULL,
        [WillingToRelocate] bit NOT NULL,
        [ExpectedSalary] nvarchar(100) NOT NULL,
        [DailyAutomationEnabled] bit NOT NULL,
        [DailyRunHourLocal] int NOT NULL,
        [TimeZoneId] nvarchar(100) NOT NULL,
        [LastDiscoveryAtUtc] datetime2 NULL,
        [LastDiscoverySummary] nvarchar(500) NOT NULL,
        [UpdatedAtUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_JobPreferences] PRIMARY KEY ([UserId])
    );
END
GO
