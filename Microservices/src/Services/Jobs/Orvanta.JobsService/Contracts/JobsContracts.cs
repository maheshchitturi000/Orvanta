using System.ComponentModel.DataAnnotations;

namespace Orvanta.JobsService.Contracts;

public sealed record ProfileDto(
    string FullName,
    string Email,
    string Phone,
    string Headline,
    string CurrentTitle,
    string Location,
    decimal TotalYearsExperience,
    IReadOnlyList<string> Skills,
    string Summary,
    string LinkedInUrl,
    string PortfolioUrl,
    string CvFileName,
    bool HasCv,
    DateTime UpdatedAtUtc);

public sealed class UpdateProfileRequest
{
    [Required, StringLength(100, MinimumLength = 2)] public string FullName { get; set; } = string.Empty;
    [Required, EmailAddress, StringLength(254)] public string Email { get; set; } = string.Empty;
    [StringLength(30)] public string Phone { get; set; } = string.Empty;
    [StringLength(200)] public string Headline { get; set; } = string.Empty;
    [StringLength(150)] public string CurrentTitle { get; set; } = string.Empty;
    [StringLength(150)] public string Location { get; set; } = string.Empty;
    [Range(0, 50)] public decimal TotalYearsExperience { get; set; }
    [MaxLength(60)] public List<string> Skills { get; set; } = [];
    [StringLength(2000)] public string Summary { get; set; } = string.Empty;
    [Url, StringLength(300)] public string? LinkedInUrl { get; set; }
    [Url, StringLength(300)] public string? PortfolioUrl { get; set; }
}

public sealed class PreferencesDto
{
    [MaxLength(10)] public List<string> DesiredTitles { get; set; } = [];
    [MaxLength(15)] public List<string> Locations { get; set; } = [];
    public bool IncludeRemote { get; set; } = true;
    public bool RemoteOnly { get; set; }
    [Range(50, 100)] public int MinMatchScore { get; set; } = 80;
    [Range(1, 90)] public int MaxPostingAgeDays { get; set; } = 30;
    [MaxLength(50)] public List<string> CompanyBoards { get; set; } = [];
    [MaxLength(30)] public List<string> ExcludedCompanies { get; set; } = [];
    [Range(0, 365)] public int NoticePeriodDays { get; set; } = 30;
    [StringLength(200)] public string WorkAuthorization { get; set; } = string.Empty;
    public bool NeedsVisaSponsorship { get; set; }
    public bool WillingToRelocate { get; set; }
    [StringLength(100)] public string ExpectedSalary { get; set; } = string.Empty;
    public bool DailyAutomationEnabled { get; set; } = true;
    [Range(0, 23)] public int DailyRunHourLocal { get; set; } = 7;
    [Required, StringLength(100)] public string TimeZoneId { get; set; } = "UTC";
    public DateTime? LastDiscoveryAtUtc { get; set; }
    public string LastDiscoverySummary { get; set; } = string.Empty;
}

public sealed record JobDto(
    string Source,
    string Company,
    string Title,
    string Location,
    bool IsRemote,
    string Url,
    string ApplyUrl,
    DateTime? PostedAtUtc);

public sealed record MatchDto(
    Guid Id,
    int Score,
    string Status,
    IReadOnlyList<string> MatchedSkills,
    IReadOnlyList<string> MissingSkills,
    IReadOnlyList<string> Reasons,
    DateTime CreatedAtUtc,
    JobDto Job);

public sealed class ApproveMatchesRequest
{
    /// <summary>Specific matches to approve. Ignored when <see cref="All"/> is true.</summary>
    [MaxLength(200)] public List<Guid> MatchIds { get; set; } = [];
    public bool All { get; set; }
}

public sealed record ApproveResult(int Approved, int Queued);

public sealed record NearMissDto(int Score, string Company, string Title, string Location, string Source, string Url, IReadOnlyList<string> Reasons);

/// <summary>An actionable tip shown when a search finds little; Link is a route in the Jobs section.</summary>
public sealed record SearchHint(string Message, string? Link);

public sealed record DiscoveryResult(
    int BoardsScanned,
    int JobsScanned,
    int NewMatches,
    IReadOnlyList<string> Errors,
    string Summary,
    IReadOnlyList<NearMissDto> NearMisses,
    IReadOnlyList<SearchHint> Hints,
    IReadOnlyList<string> RolesUsed,
    bool SearchConnected);

/// <summary>Connection state of a job search source. ServerKey = using the server-wide key, not the user's own.</summary>
public sealed record SourceStatusDto(string Name, string DisplayName, bool Connected, bool ServerKey, DateTime? ConnectedAtUtc);

public sealed class ConnectSourceRequest
{
    [Required(ErrorMessage = "Paste your API key."), StringLength(200, MinimumLength = 10, ErrorMessage = "That doesn't look like an API key.")]
    public string ApiKey { get; set; } = string.Empty;
}

public sealed record ApplicationDto(
    Guid Id,
    string Status,
    string Channel,
    string CoverLetter,
    IReadOnlyDictionary<string, string> Answers,
    string Error,
    DateTime ApprovedAtUtc,
    DateTime? PreparedAtUtc,
    DateTime? SubmittedAtUtc,
    JobDto Job);

public sealed class ReportFailureRequest
{
    [Required, StringLength(500)] public string Reason { get; set; } = string.Empty;
}

public sealed record JobsSummary(
    bool HasCv,
    bool HasPreferences,
    int NewMatches,
    int Queued,
    int ReadyToSubmit,
    int Submitted,
    int Failed,
    bool DailyAutomationEnabled,
    DateTime? LastDiscoveryAtUtc,
    string LastDiscoverySummary);
