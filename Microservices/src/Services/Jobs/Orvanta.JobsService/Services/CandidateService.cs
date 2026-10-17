using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Orvanta.Jobs.Core;
using Orvanta.Jobs.Core.Cv;
using Orvanta.Jobs.Core.Data;
using Orvanta.Jobs.Core.Domain;
using Orvanta.Jobs.Core.Matching;
using Orvanta.Jobs.Core.Sources;
using Orvanta.JobsService.Contracts;
using Orvanta.Shared.Responses;

namespace Orvanta.JobsService.Services;

/// <summary>CV upload/parsing, profile and preferences.</summary>
public sealed class CandidateService(
    JobsDbContext db,
    ProfileExtractor extractor,
    IOptions<JobsOptions> options,
    ILogger<CandidateService> logger)
{
    public async Task<ServiceResult<ProfileDto>> GetProfileAsync(Guid userId, CancellationToken ct)
    {
        var profile = await db.Profiles.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == userId, ct);
        return profile is null
            ? ServiceResult<ProfileDto>.Fail(404, "Upload your CV to get started.")
            : ServiceResult<ProfileDto>.Ok(ToDto(profile));
    }

    public async Task<ServiceResult<ProfileDto>> UploadCvAsync(Guid userId, IFormFile? file, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return ServiceResult<ProfileDto>.Fail(400, "Choose a CV file to upload.");
        if (file.Length > CvTextExtractor.MaxBytes)
            return ServiceResult<ProfileDto>.Fail(400, "CV must be 5 MB or smaller.");

        var extension = Path.GetExtension(file.FileName);
        if (!CvTextExtractor.SupportedTypes.TryGetValue(extension, out var contentType))
            return ServiceResult<ProfileDto>.Fail(400, "Upload your CV as a PDF, DOCX or TXT file.");

        byte[] bytes;
        await using (var stream = file.OpenReadStream())
        using (var memory = new MemoryStream())
        {
            await stream.CopyToAsync(memory, ct);
            bytes = memory.ToArray();
        }

        string text;
        try
        {
            text = CvTextExtractor.Extract(file.FileName, bytes);
        }
        catch (UnsupportedCvException ex)
        {
            return ServiceResult<ProfileDto>.Fail(422, ex.Message);
        }

        var extracted = await extractor.ExtractAsync(text, ct);

        var profile = await db.Profiles.FirstOrDefaultAsync(p => p.UserId == userId, ct);
        if (profile is null)
        {
            profile = new CandidateProfile { UserId = userId };
            db.Profiles.Add(profile);
        }

        // Re-uploading refreshes parsed fields but keeps values the user typed in where parsing found nothing.
        profile.FullName = Pick(extracted.FullName, profile.FullName, 100);
        profile.Email = Pick(extracted.Email, profile.Email, 254);
        profile.Phone = Pick(extracted.Phone, profile.Phone, 30);
        profile.Headline = Pick(extracted.Headline, profile.Headline, 200);
        profile.CurrentTitle = Pick(extracted.CurrentTitle, profile.CurrentTitle, 150);
        profile.Location = Pick(extracted.Location, profile.Location, 150);
        profile.Summary = Pick(extracted.Summary, profile.Summary, 2000);
        profile.LinkedInUrl = Pick(extracted.LinkedInUrl, profile.LinkedInUrl, 300);
        profile.TotalYearsExperience = extracted.TotalYearsExperience > 0 ? extracted.TotalYearsExperience : profile.TotalYearsExperience;
        profile.Skills = extracted.Skills.Count > 0 ? extracted.Skills.ToList() : profile.Skills;
        profile.CvFileName = Path.GetFileName(file.FileName) is { Length: <= 200 } name ? name : $"cv{extension}";
        profile.CvContentType = contentType;
        profile.CvFile = bytes;
        profile.CvText = text;
        profile.UpdatedAtUtc = DateTime.UtcNow;

        await EnsurePreferencesAsync(userId, profile, ct);
        await db.SaveChangesAsync(ct);

        logger.LogInformation("CV parsed for {UserId} ({Skills} skills, AI: {UsedAi})", userId, profile.Skills.Count, extracted.UsedAi);
        return ServiceResult<ProfileDto>.Ok(ToDto(profile),
            extracted.UsedAi ? "CV analysed. Please review the details below." : "CV read. Please review and complete the details below.");
    }

    public async Task<ServiceResult<ProfileDto>> UpdateProfileAsync(Guid userId, UpdateProfileRequest request, CancellationToken ct)
    {
        var profile = await db.Profiles.FirstOrDefaultAsync(p => p.UserId == userId, ct);
        if (profile is null)
            return ServiceResult<ProfileDto>.Fail(404, "Upload your CV first.");

        profile.FullName = request.FullName.Trim();
        profile.Email = request.Email.Trim();
        profile.Phone = request.Phone.Trim();
        profile.Headline = request.Headline.Trim();
        profile.CurrentTitle = request.CurrentTitle.Trim();
        profile.Location = request.Location.Trim();
        profile.TotalYearsExperience = request.TotalYearsExperience;
        profile.Skills = Clean(request.Skills.Select(SkillCatalog.Normalize), 60, 50);
        profile.Summary = request.Summary.Trim();
        profile.LinkedInUrl = request.LinkedInUrl?.Trim() ?? string.Empty;
        profile.PortfolioUrl = request.PortfolioUrl?.Trim() ?? string.Empty;
        profile.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        return ServiceResult<ProfileDto>.Ok(ToDto(profile), "Profile saved.");
    }

    public async Task<(string FileName, string ContentType, byte[] Content)?> GetCvFileAsync(Guid userId, CancellationToken ct)
    {
        var cv = await db.Profiles.AsNoTracking()
            .Where(p => p.UserId == userId && p.CvFile.Length > 0)
            .Select(p => new { p.CvFileName, p.CvContentType, p.CvFile })
            .FirstOrDefaultAsync(ct);
        return cv is null ? null : (cv.CvFileName, cv.CvContentType, cv.CvFile);
    }

    public async Task<ServiceResult<PreferencesDto>> GetPreferencesAsync(Guid userId, CancellationToken ct)
    {
        var preference = await db.Preferences.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == userId, ct);
        return ServiceResult<PreferencesDto>.Ok(ToDto(preference ?? new JobPreference { UserId = userId, CompanyBoards = options.Value.DefaultBoards.ToList() }));
    }

    public async Task<ServiceResult<PreferencesDto>> SavePreferencesAsync(Guid userId, PreferencesDto request, CancellationToken ct)
    {
        if (!IsValidTimeZone(request.TimeZoneId))
            return ServiceResult<PreferencesDto>.Fail(400, "Choose a valid time zone.");

        var invalidBoard = request.CompanyBoards.FirstOrDefault(b => !CompanyBoard.TryParse(b, out _));
        if (invalidBoard is not null)
            return ServiceResult<PreferencesDto>.Fail(400, $"\"{invalidBoard}\" isn't a valid board. Use provider:board, e.g. greenhouse:stripe.");

        var preference = await db.Preferences.FirstOrDefaultAsync(p => p.UserId == userId, ct);
        if (preference is null)
        {
            preference = new JobPreference { UserId = userId };
            db.Preferences.Add(preference);
        }

        preference.DesiredTitles = Clean(request.DesiredTitles, 10, 100);
        preference.Locations = Clean(request.Locations, 15, 100);
        preference.IncludeRemote = request.IncludeRemote;
        preference.RemoteOnly = request.RemoteOnly;
        preference.MinMatchScore = request.MinMatchScore;
        preference.MaxPostingAgeDays = request.MaxPostingAgeDays;
        preference.CompanyBoards = Clean(request.CompanyBoards.Select(b => CompanyBoard.TryParse(b, out var x) ? x.ToString() : b), 50, 120);
        preference.ExcludedCompanies = Clean(request.ExcludedCompanies, 30, 100);
        preference.NoticePeriodDays = request.NoticePeriodDays;
        preference.WorkAuthorization = request.WorkAuthorization.Trim();
        preference.NeedsVisaSponsorship = request.NeedsVisaSponsorship;
        preference.WillingToRelocate = request.WillingToRelocate;
        preference.ExpectedSalary = request.ExpectedSalary.Trim();
        preference.DailyAutomationEnabled = request.DailyAutomationEnabled;
        preference.DailyRunHourLocal = request.DailyRunHourLocal;
        preference.TimeZoneId = request.TimeZoneId;
        preference.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        return ServiceResult<PreferencesDto>.Ok(ToDto(preference), "Preferences saved.");
    }

    public IReadOnlyList<string> SuggestedBoards() => options.Value.DefaultBoards;

    private async Task EnsurePreferencesAsync(Guid userId, CandidateProfile profile, CancellationToken ct)
    {
        if (await db.Preferences.AnyAsync(p => p.UserId == userId, ct)) return;

        db.Preferences.Add(new JobPreference
        {
            UserId = userId,
            // Start with roles derived from the CV title and skills; the user can edit them.
            DesiredTitles = RoleSuggester.Suggest(profile).ToList(),
            Locations = string.IsNullOrWhiteSpace(profile.Location) ? [] : [profile.Location],
            CompanyBoards = options.Value.DefaultBoards.ToList(),
            MinMatchScore = options.Value.DefaultMatchThreshold
        });
    }

    private static bool IsValidTimeZone(string id)
    {
        try
        {
            TimeZoneInfo.FindSystemTimeZoneById(id);
            return true;
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return false;
        }
    }

    private static List<string> Clean(IEnumerable<string> values, int maxItems, int maxLength) =>
        values.Select(v => v.Trim())
            .Where(v => v.Length is > 0)
            .Select(v => v.Length > maxLength ? v[..maxLength] : v)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(maxItems)
            .ToList();

    private static string Pick(string parsed, string current, int max)
    {
        var value = string.IsNullOrWhiteSpace(parsed) ? current : parsed.Trim();
        return value.Length <= max ? value : value[..max];
    }

    private static ProfileDto ToDto(CandidateProfile p) => new(
        p.FullName, p.Email, p.Phone, p.Headline, p.CurrentTitle, p.Location, p.TotalYearsExperience, p.Skills,
        p.Summary, p.LinkedInUrl, p.PortfolioUrl, p.CvFileName, p.CvFile.Length > 0, p.UpdatedAtUtc);

    private static PreferencesDto ToDto(JobPreference p) => new()
    {
        DesiredTitles = p.DesiredTitles,
        Locations = p.Locations,
        IncludeRemote = p.IncludeRemote,
        RemoteOnly = p.RemoteOnly,
        MinMatchScore = p.MinMatchScore,
        MaxPostingAgeDays = p.MaxPostingAgeDays,
        CompanyBoards = p.CompanyBoards,
        ExcludedCompanies = p.ExcludedCompanies,
        NoticePeriodDays = p.NoticePeriodDays,
        WorkAuthorization = p.WorkAuthorization,
        NeedsVisaSponsorship = p.NeedsVisaSponsorship,
        WillingToRelocate = p.WillingToRelocate,
        ExpectedSalary = p.ExpectedSalary,
        DailyAutomationEnabled = p.DailyAutomationEnabled,
        DailyRunHourLocal = p.DailyRunHourLocal,
        TimeZoneId = p.TimeZoneId,
        LastDiscoveryAtUtc = p.LastDiscoveryAtUtc,
        LastDiscoverySummary = p.LastDiscoverySummary
    };
}
