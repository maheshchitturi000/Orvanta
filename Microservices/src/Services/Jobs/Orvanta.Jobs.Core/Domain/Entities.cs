namespace Orvanta.Jobs.Core.Domain;

/// <summary>Candidate details parsed from the CV and corrected by the user.</summary>
public sealed class CandidateProfile
{
    public Guid UserId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Headline { get; set; } = string.Empty;
    public string CurrentTitle { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public decimal TotalYearsExperience { get; set; }
    public List<string> Skills { get; set; } = [];
    public string Summary { get; set; } = string.Empty;
    public string LinkedInUrl { get; set; } = string.Empty;
    public string PortfolioUrl { get; set; } = string.Empty;

    public string CvFileName { get; set; } = string.Empty;
    public string CvContentType { get; set; } = string.Empty;
    public byte[] CvFile { get; set; } = [];
    public string CvText { get; set; } = string.Empty;

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>What the user is looking for and how the daily automation should run.</summary>
public sealed class JobPreference
{
    public Guid UserId { get; set; }
    public List<string> DesiredTitles { get; set; } = [];
    public List<string> Locations { get; set; } = [];
    public bool IncludeRemote { get; set; } = true;
    public bool RemoteOnly { get; set; }
    public int MinMatchScore { get; set; } = 80;
    public int MaxPostingAgeDays { get; set; } = 30;

    /// <summary>Career boards to scan, as "provider:board", e.g. "greenhouse:stripe". Empty = defaults.</summary>
    public List<string> CompanyBoards { get; set; } = [];
    public List<string> ExcludedCompanies { get; set; } = [];

    // Answers commonly asked by application forms.
    public int NoticePeriodDays { get; set; } = 30;
    public string WorkAuthorization { get; set; } = string.Empty;
    public bool NeedsVisaSponsorship { get; set; }
    public bool WillingToRelocate { get; set; }
    public string ExpectedSalary { get; set; } = string.Empty;

    // Daily automation
    public bool DailyAutomationEnabled { get; set; } = true;
    public int DailyRunHourLocal { get; set; } = 7;
    public string TimeZoneId { get; set; } = "UTC";
    public DateTime? LastDiscoveryAtUtc { get; set; }
    public string LastDiscoverySummary { get; set; } = string.Empty;

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>A job found on a career board. Stored only when it matched at least one user.</summary>
public sealed class JobPosting
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Source { get; set; } = string.Empty;
    public string ExternalId { get; set; } = string.Empty;
    public string Company { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public bool IsRemote { get; set; }
    public string Url { get; set; } = string.Empty;
    public string ApplyUrl { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DateTime? PostedAtUtc { get; set; }
    public DateTime FetchedAtUtc { get; set; } = DateTime.UtcNow;
}

public enum MatchStatus
{
    New = 0,
    Approved = 1,
    Skipped = 2
}

public sealed class JobMatch
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid JobPostingId { get; set; }
    public JobPosting? JobPosting { get; set; }
    public int Score { get; set; }
    public List<string> MatchedSkills { get; set; } = [];
    public List<string> MissingSkills { get; set; } = [];
    public List<string> Reasons { get; set; } = [];
    public MatchStatus Status { get; set; } = MatchStatus.New;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public enum ApplicationStatus
{
    /// <summary>Approved by the user, waiting for the background worker.</summary>
    Queued = 0,
    /// <summary>Worker is preparing the cover letter and answers.</summary>
    Preparing = 1,
    /// <summary>Package ready; the Chrome extension submits it in the user's browser.</summary>
    ReadyToSubmit = 2,
    Submitted = 3,
    Failed = 4
}

public sealed class JobApplication
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid JobMatchId { get; set; }
    public Guid JobPostingId { get; set; }
    public JobPosting? JobPosting { get; set; }
    public ApplicationStatus Status { get; set; } = ApplicationStatus.Queued;

    /// <summary>"extension" (ATS / LinkedIn form autofill in the user's browser) or "email".</summary>
    public string Channel { get; set; } = "extension";
    public string CoverLetter { get; set; } = string.Empty;

    /// <summary>Prepared answers for common form fields, as JSON.</summary>
    public string AnswersJson { get; set; } = "{}";
    public string Error { get; set; } = string.Empty;
    public int Attempts { get; set; }

    public DateTime ApprovedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? PreparedAtUtc { get; set; }
    public DateTime? SubmittedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
