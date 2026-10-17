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
public sealed class MatchService(JobsDbContext db, IJobQueue queue, ILogger<MatchService> logger)
{
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

    public async Task<ServiceResult<object>> RunDiscoveryAsync(Guid userId, CancellationToken ct)
    {
        if (!await db.Profiles.AnyAsync(p => p.UserId == userId, ct))
            return ServiceResult<object>.Fail(409, "Upload your CV before searching for jobs.");

        try
        {
            await queue.EnqueueDiscoveryAsync(new DiscoveryRequest(userId, "manual"), ct);
            return ServiceResult<object>.Ok(null!, "Searching career boards in the background. New matches appear here in a minute or two.");
        }
        catch (Exception ex) when (ex is RequestFailedException or AggregateException or HttpRequestException)
        {
            logger.LogError(ex, "Discovery queue unavailable");
            return ServiceResult<object>.Fail(503, "The background service is unavailable. Make sure Azurite and the Jobs Functions are running.");
        }
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
