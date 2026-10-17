using System.Collections.Concurrent;
using System.Text.Json;
using Azure;
using Microsoft.EntityFrameworkCore;
using Orvanta.Jobs.Core;
using Orvanta.Jobs.Core.Data;
using Orvanta.Jobs.Core.Domain;
using Orvanta.Jobs.Core.Workflow;
using Orvanta.JobsService.Contracts;
using Orvanta.Shared.Responses;

namespace Orvanta.JobsService.Services;

/// <summary>Matches, one-click approval, applications and the hand-off to the background workflow.</summary>
public sealed class MatchService(JobsDbContext db, IJobQueue queue, DiscoveryWorkflow discovery, ILogger<MatchService> logger)
{
    private static readonly TimeSpan InteractiveSearchTimeout = TimeSpan.FromSeconds(90);

    // One interactive search per user at a time (double clicks, two tabs). Per instance; the
    // workflow itself is idempotent, so a rare overlap across instances is harmless.
    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> SearchLocks = new();
    public async Task<IReadOnlyList<MatchDto>> ListMatchesAsync(Guid userId, MatchStatus? status, CancellationToken ct)
    {
        var query = db.Matches.AsNoTracking().Include(m => m.JobPosting).Where(m => m.UserId == userId);
        if (status is not null)
        {
            query = query.Where(m => m.Status == status);
        }

        var matches = await query.OrderByDescending(m => m.Score).ThenByDescending(m => m.CreatedAtUtc).Take(200).ToListAsync(ct);
        return matches.Select(ToDto).ToList();
    }

    public async Task<ServiceResult<ApproveResult>> ApproveAsync(Guid userId, ApproveMatchesRequest request, CancellationToken ct)
    {
        var query = db.Matches.Where(m => m.UserId == userId && m.Status == MatchStatus.New);
        if (!request.All)
        {
            if (request.MatchIds.Count == 0)
                return ServiceResult<ApproveResult>.Fail(400, "Select at least one job to approve.");
            query = query.Where(m => request.MatchIds.Contains(m.Id));
        }

        var matches = await query.ToListAsync(ct);
        if (matches.Count == 0)
            return ServiceResult<ApproveResult>.Fail(404, "There are no new matches to approve.");

        var existing = (await db.Applications
            .Where(a => a.UserId == userId)
            .Select(a => a.JobMatchId)
            .ToListAsync(ct)).ToHashSet();

        var applications = new List<JobApplication>();
        foreach (var match in matches)
        {
            match.Status = MatchStatus.Approved;
            if (existing.Contains(match.Id)) continue;

            var application = new JobApplication { UserId = userId, JobMatchId = match.Id, JobPostingId = match.JobPostingId };
            db.Applications.Add(application);
            applications.Add(application);
        }

        // Save first (source of truth), then hand off. If the queue is down, the scheduler re-queues stale items.
        await db.SaveChangesAsync(ct);

        var queued = 0;
        foreach (var application in applications)
        {
            try
            {
                await queue.EnqueueApplyAsync(new ApplyRequest(application.Id), ct);
                queued++;
            }
            catch (Exception ex) when (ex is RequestFailedException or AggregateException or HttpRequestException)
            {
                logger.LogWarning(ex, "Could not queue application {ApplicationId}; the scheduler will retry", application.Id);
            }
        }

        var message = queued == applications.Count
            ? $"Approved {matches.Count} job(s). Orvanta is preparing your applications in the background."
            : $"Approved {matches.Count} job(s). {applications.Count - queued} will start shortly (background service is busy).";
        return ServiceResult<ApproveResult>.Ok(new ApproveResult(matches.Count, queued), message);
    }

    public async Task<ServiceResult<object>> SkipAsync(Guid userId, Guid matchId, CancellationToken ct)
    {
        var match = await db.Matches.FirstOrDefaultAsync(m => m.Id == matchId && m.UserId == userId, ct);
        if (match is null) return ServiceResult<object>.Fail(404, "Match not found.");
        if (match.Status != MatchStatus.New) return ServiceResult<object>.Fail(409, "This job was already handled.");

        match.Status = MatchStatus.Skipped;
        await db.SaveChangesAsync(ct);
        return ServiceResult<object>.Ok(null!, "Job skipped.");
    }

    /// <summary>
    /// "Search now": runs discovery immediately and returns the outcome, because the user is waiting
    /// for it. The daily search uses the same workflow from the background Functions instead.
    /// </summary>
    public async Task<ServiceResult<DiscoveryResult>> SearchNowAsync(Guid userId, CancellationToken ct)
    {
        if (!await db.Profiles.AnyAsync(p => p.UserId == userId, ct))
            return ServiceResult<DiscoveryResult>.Fail(409, "Upload your CV before searching for jobs.");

        var gate = SearchLocks.GetOrAdd(userId, _ => new SemaphoreSlim(1, 1));
        if (!await gate.WaitAsync(0, ct))
            return ServiceResult<DiscoveryResult>.Fail(409, "A search is already running. Results will appear in a moment.");

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(InteractiveSearchTimeout);

            var outcome = await discovery.RunAsync(userId, timeout.Token);
            var preference = await db.Preferences.AsNoTracking().FirstAsync(p => p.UserId == userId, ct);

            var nearMisses = outcome.NearMisses
                .Select(n => new NearMissDto(n.Match.Score, n.Job.Company, n.Job.Title, n.Job.Location, n.Job.Source, n.Job.Url, n.Match.Reasons))
                .ToList();
            var result = new DiscoveryResult(outcome.BoardsScanned, outcome.JobsScanned, outcome.NewMatches, outcome.Errors,
                outcome.Summary, nearMisses, Hints(preference, outcome));

            var message = outcome.NewMatches > 0
                ? $"Found {outcome.NewMatches} new match{(outcome.NewMatches == 1 ? "" : "es")} in {outcome.JobsScanned:N0} jobs."
                : $"No new matches in {outcome.JobsScanned:N0} jobs right now. Try more target roles or boards.";
            return ServiceResult<DiscoveryResult>.Ok(result, message);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return ServiceResult<DiscoveryResult>.Fail(504, "The search took too long. Try again, or remove slow boards from your preferences.");
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>Concrete next steps, most impactful first, when a search returns few matches.</summary>
    private List<SearchHint> Hints(JobPreference preference, DiscoveryOutcome outcome)
    {
        var hints = new List<SearchHint>();
        if (outcome.NewMatches >= 5) return hints;

        if (preference.DesiredTitles.Count == 0)
            hints.Add(new("Add your target roles (e.g. .NET Developer, Full Stack Developer). Without them only your CV title is matched.", "profile"));

        if (!discovery.SearchSourcesConfigured)
            hints.Add(new("LinkedIn, Naukri and Indeed search is off: add your free JSearch API key (see README → Job sources). Right now only company career boards are scanned.", null));

        // Only suggest lowering the bar for genuine near misses, not jobs held back on purpose
        // (wrong role, missing core skill, far more experience required).
        var closest = outcome.NearMisses.Where(n => !n.Match.Capped).Select(n => n.Match.Score).DefaultIfEmpty(0).Max();
        if (closest >= 70 && preference.MinMatchScore > closest)
            hints.Add(new($"Your closest jobs scored {closest}%. Lower the minimum match to {closest / 5 * 5}% to review them.", "profile"));

        if (preference.Locations.Count > 0 && !preference.IncludeRemote)
            hints.Add(new("Turn on \"Include remote jobs\" to also see remote roles open to your country.", "profile"));

        return hints;
    }

    public async Task<IReadOnlyList<ApplicationDto>> ListApplicationsAsync(Guid userId, ApplicationStatus? status, CancellationToken ct)
    {
        var query = db.Applications.AsNoTracking().Include(a => a.JobPosting).Where(a => a.UserId == userId);
        if (status is not null)
        {
            query = query.Where(a => a.Status == status);
        }

        var items = await query.OrderByDescending(a => a.UpdatedAtUtc).Take(200).ToListAsync(ct);
        return items.Select(ToDto).ToList();
    }

    /// <summary>Called by the Chrome extension after it submitted the form in the user's browser.</summary>
    public Task<ServiceResult<object>> MarkSubmittedAsync(Guid userId, Guid applicationId, CancellationToken ct) =>
        UpdateApplicationAsync(userId, applicationId, a =>
        {
            a.Status = ApplicationStatus.Submitted;
            a.SubmittedAtUtc = DateTime.UtcNow;
            a.Error = string.Empty;
        }, "Marked as submitted.", ct);

    public Task<ServiceResult<object>> MarkFailedAsync(Guid userId, Guid applicationId, string reason, CancellationToken ct) =>
        UpdateApplicationAsync(userId, applicationId, a =>
        {
            a.Status = ApplicationStatus.Failed;
            a.Error = reason.Trim();
        }, "Marked as failed.", ct);

    public async Task<JobsSummary> SummaryAsync(Guid userId, CancellationToken ct)
    {
        var hasCv = await db.Profiles.AnyAsync(p => p.UserId == userId, ct);
        var preference = await db.Preferences.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == userId, ct);
        var newMatches = await db.Matches.CountAsync(m => m.UserId == userId && m.Status == MatchStatus.New, ct);
        var counts = await db.Applications.Where(a => a.UserId == userId)
            .GroupBy(a => a.Status)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);

        int Count(ApplicationStatus s) => counts.GetValueOrDefault(s);

        return new JobsSummary(
            hasCv,
            preference is not null,
            newMatches,
            Count(ApplicationStatus.Queued) + Count(ApplicationStatus.Preparing),
            Count(ApplicationStatus.ReadyToSubmit),
            Count(ApplicationStatus.Submitted),
            Count(ApplicationStatus.Failed),
            preference?.DailyAutomationEnabled ?? false,
            preference?.LastDiscoveryAtUtc,
            preference?.LastDiscoverySummary ?? string.Empty);
    }

    private async Task<ServiceResult<object>> UpdateApplicationAsync(
        Guid userId, Guid applicationId, Action<JobApplication> change, string message, CancellationToken ct)
    {
        var application = await db.Applications.FirstOrDefaultAsync(a => a.Id == applicationId && a.UserId == userId, ct);
        if (application is null) return ServiceResult<object>.Fail(404, "Application not found.");

        change(application);
        application.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return ServiceResult<object>.Ok(null!, message);
    }

    private static JobDto ToJobDto(JobPosting p) =>
        new(p.Source, p.Company, p.Title, p.Location, p.IsRemote, p.Url, p.ApplyUrl, p.PostedAtUtc);

    private static MatchDto ToDto(JobMatch m) =>
        new(m.Id, m.Score, m.Status.ToString(), m.MatchedSkills, m.MissingSkills, m.Reasons, m.CreatedAtUtc, ToJobDto(m.JobPosting!));

    private static ApplicationDto ToDto(JobApplication a) =>
        new(a.Id, a.Status.ToString(), a.Channel, a.CoverLetter,
            JsonSerializer.Deserialize<Dictionary<string, string>>(a.AnswersJson) ?? [],
            a.Error, a.ApprovedAtUtc, a.PreparedAtUtc, a.SubmittedAtUtc, ToJobDto(a.JobPosting!));
}
