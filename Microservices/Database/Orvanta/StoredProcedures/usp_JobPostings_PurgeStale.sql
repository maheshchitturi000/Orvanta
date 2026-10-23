SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO
-- Housekeeping (schedule weekly): removes postings fetched more than @OlderThanDays ago that nobody applied to.
-- Matches cascade with the posting; postings referenced by an application are always kept.
-- Example: EXEC dbo.usp_JobPostings_PurgeStale @OlderThanDays = 60;
CREATE OR ALTER PROCEDURE dbo.usp_JobPostings_PurgeStale
    @OlderThanDays INT = 60,
    @BatchSize     INT = 2000
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @cutoff DATETIME2 = DATEADD(DAY, -@OlderThanDays, SYSUTCDATETIME());
    DECLARE @deleted INT = 1, @total INT = 0;

    WHILE @deleted > 0
    BEGIN
        DELETE TOP (@BatchSize) p
        FROM dbo.JobPostings AS p
        WHERE p.FetchedAtUtc < @cutoff
          AND NOT EXISTS (SELECT 1 FROM dbo.JobApplications AS a WHERE a.JobPostingId = p.Id);
        SET @deleted = @@ROWCOUNT;
        SET @total += @deleted;
    END

    SELECT @total AS JobPostingsDeleted;
END
GO
