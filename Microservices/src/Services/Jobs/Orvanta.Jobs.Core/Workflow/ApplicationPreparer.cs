using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Orvanta.Jobs.Core.Ai;
using Orvanta.Jobs.Core.Data;
using Orvanta.Jobs.Core.Domain;

namespace Orvanta.Jobs.Core.Workflow;

/// <summary>
/// Background step for one approved job: writes a tailored cover letter and the
/// answers application forms usually ask, then marks the application ready for
/// the Chrome extension to submit in the user's own browser session.
/// Safe to retry: it only acts on Queued/Preparing applications.
/// </summary>
public sealed class ApplicationPreparer(JobsDbContext db, ILlmClient llm, ILogger<ApplicationPreparer> logger)
{
    private const string CoverLetterPrompt =
        "You write concise, specific cover letters (max 180 words, 3 short paragraphs, no placeholders, no invented facts). " +
        "Use only the candidate details given. Plain text only.";

    public async Task PrepareAsync(Guid applicationId, CancellationToken ct)
    {
        var application = await db.Applications.Include(a => a.JobPosting).FirstOrDefaultAsync(a => a.Id == applicationId, ct);
        if (application is null)
        {
            logger.LogWarning("Application {ApplicationId} not found; message ignored", applicationId);
            return;
        }

        if (application.Status is not (ApplicationStatus.Queued or ApplicationStatus.Preparing))
        {
            logger.LogInformation("Application {ApplicationId} already {Status}; nothing to do", applicationId, application.Status);
            return;
        }

        var profile = await db.Profiles.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == application.UserId, ct);
        var preference = await db.Preferences.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == application.UserId, ct);
        if (profile is null || application.JobPosting is null)
        {
            Fail(application, "Profile or job is missing.");
            await db.SaveChangesAsync(ct);
            return;
        }

        application.Status = ApplicationStatus.Preparing;
        application.Attempts++;
        application.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        var job = application.JobPosting;
        application.CoverLetter = await WriteCoverLetterAsync(profile, job, ct);
        application.AnswersJson = JsonSerializer.Serialize(BuildAnswers(profile, preference, job));
        application.Channel = "extension";
        application.Status = ApplicationStatus.ReadyToSubmit;
        application.PreparedAtUtc = DateTime.UtcNow;
        application.UpdatedAtUtc = DateTime.UtcNow;
        application.Error = string.Empty;
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Application {ApplicationId} ready for {Company} – {Title}", applicationId, job.Company, job.Title);
    }

    /// <summary>Called when retries are exhausted (poison queue).</summary>
    public async Task MarkFailedAsync(Guid applicationId, string reason, CancellationToken ct)
    {
        var application = await db.Applications.FirstOrDefaultAsync(a => a.Id == applicationId, ct);
        if (application is null || application.Status is ApplicationStatus.Submitted or ApplicationStatus.ReadyToSubmit) return;

        Fail(application, reason);
        await db.SaveChangesAsync(ct);
    }

    private async Task<string> WriteCoverLetterAsync(CandidateProfile profile, JobPosting job, CancellationToken ct)
    {
        var description = job.Description.Length > 4000 ? job.Description[..4000] : job.Description;
        var prompt =
            $"Candidate: {profile.FullName}, {profile.CurrentTitle}, {profile.TotalYearsExperience:0.#} years experience.\n" +
            $"Skills: {string.Join(", ", profile.Skills.Take(25))}\n" +
            $"Summary: {profile.Summary}\n\n" +
            $"Job: {job.Title} at {job.Company} ({job.Location})\n{description}";

        var letter = await llm.CompleteAsync(CoverLetterPrompt, prompt, json: false, ct);
        return string.IsNullOrWhiteSpace(letter) ? TemplateCoverLetter(profile, job) : letter.Trim();
    }

    public static string TemplateCoverLetter(CandidateProfile profile, JobPosting job)
    {
        var skills = string.Join(", ", profile.Skills.Where(s => job.Description.Contains(s, StringComparison.OrdinalIgnoreCase)).Take(5));
        var years = profile.TotalYearsExperience > 0 ? $"{profile.TotalYearsExperience:0.#} years of experience" : "hands-on experience";
        return
            $"Dear {job.Company} Hiring Team,\n\n" +
            $"I'm applying for the {job.Title} role. I'm a {(string.IsNullOrWhiteSpace(profile.CurrentTitle) ? "software professional" : profile.CurrentTitle)} with {years}" +
            (skills.Length > 0 ? $", working with {skills}.\n\n" : ".\n\n") +
            "The responsibilities in this role match the work I enjoy and have delivered before, and I'd welcome the chance to contribute to your team.\n\n" +
            $"Thank you for your consideration.\n{profile.FullName}";
    }

    public static Dictionary<string, string> BuildAnswers(CandidateProfile profile, JobPreference? preference, JobPosting job)
    {
        var names = profile.FullName.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        return new Dictionary<string, string>
        {
            ["firstName"] = names.ElementAtOrDefault(0) ?? string.Empty,
            ["lastName"] = names.ElementAtOrDefault(1) ?? string.Empty,
            ["fullName"] = profile.FullName,
            ["email"] = profile.Email,
            ["phone"] = profile.Phone,
            ["location"] = profile.Location,
            ["currentTitle"] = profile.CurrentTitle,
            ["yearsOfExperience"] = profile.TotalYearsExperience.ToString("0.#"),
            ["linkedInUrl"] = profile.LinkedInUrl,
            ["portfolioUrl"] = profile.PortfolioUrl,
            ["noticePeriodDays"] = (preference?.NoticePeriodDays ?? 30).ToString(),
            ["workAuthorization"] = preference?.WorkAuthorization ?? string.Empty,
            ["needsVisaSponsorship"] = (preference?.NeedsVisaSponsorship ?? false) ? "Yes" : "No",
            ["willingToRelocate"] = (preference?.WillingToRelocate ?? false) ? "Yes" : "No",
            ["expectedSalary"] = preference?.ExpectedSalary ?? string.Empty,
            ["company"] = job.Company,
            ["jobTitle"] = job.Title
        };
    }

    private static void Fail(JobApplication application, string reason)
    {
        application.Status = ApplicationStatus.Failed;
        application.Error = reason.Length > 1000 ? reason[..1000] : reason;
        application.UpdatedAtUtc = DateTime.UtcNow;
    }
}
