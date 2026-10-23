SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO
-- Pipeline counts for one user. Status values mirror Orvanta.Jobs.Core.Domain:
--   MatchStatus:       0 New, 1 Approved, 2 Skipped
--   ApplicationStatus: 0 Queued, 1 Preparing, 2 ReadyToSubmit, 3 Submitted, ... (see Entities.cs)
-- Example: SELECT * FROM dbo.ufn_JobPipeline_UserSummary(@UserId);
CREATE OR ALTER FUNCTION dbo.ufn_JobPipeline_UserSummary (@UserId UNIQUEIDENTIFIER)
RETURNS TABLE
AS
RETURN
    SELECT N'Match' AS Stage, Status, COUNT(*) AS Total, AVG(Score) AS AvgScore
    FROM dbo.JobMatches
    WHERE UserId = @UserId
    GROUP BY Status
    UNION ALL
    SELECT N'Application', Status, COUNT(*), NULL
    FROM dbo.JobApplications
    WHERE UserId = @UserId
    GROUP BY Status;
GO
