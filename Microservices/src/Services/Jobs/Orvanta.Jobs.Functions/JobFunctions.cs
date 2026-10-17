using Microsoft.Azure.Functions.Worker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Orvanta.Jobs.Core;
using Orvanta.Jobs.Core.Data;
using Orvanta.Jobs.Core.Domain;
using Orvanta.Jobs.Core.Workflow;

namespace Orvanta.Jobs.Functions;

/// <summary>
/// Background workflow for job automation.
///
///   Scheduler (hourly timer) ──► job-discovery queue ──► DiscoveryWorker ──► new matches
///   User clicks "Approve all" (API) ──► job-apply queue ──► ApplyWorker ──► ReadyToSubmit
///   ApplyWorker fails 5× ──► job-apply-poison ──► ApplyPoisonHandler ──► Failed
///
/// Every step is idempotent, so queue retries and re-runs are safe.
/// </summary>
public sealed class JobFunctions(
    JobsDbContext db,
    IJobQueue queue,
    DiscoveryWorkflow discovery,
    ApplicationPreparer preparer,
    ILogger<JobFunctions> logger)
{
    private static readonly TimeSpan StaleQueuedAfter = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan MinTimeBetweenDailyRuns = TimeSpan.FromHours(20);

    /// <summary>
    /// Runs at the top of every hour. Starts discovery for users whose local time
    /// equals their chosen hour (international time zones), and re-queues applications
    /// that were saved but never reached the queue (outbox-style safety net).
    /// </summary>
    [Function(nameof(Scheduler))]
    public async Task Scheduler([TimerTrigger("0 0 * * * *")] TimerInfo timer, CancellationToken ct)
    {
        var now = DateTime.UtcNow;

        var candidates = await db.Preferences.AsNoTracking()
            .Where(p => p.DailyAutomationEnabled)
            .Select(p => new { p.UserId, p.TimeZoneId, p.DailyRunHourLocal, p.LastDiscoveryAtUtc })
            .ToListAsync(ct);

        var due = candidates
            .Where(p => LocalHour(now, p.TimeZoneId) == p.DailyRunHourLocal)
            .Where(p => p.LastDiscoveryAtUtc is null || now - p.LastDiscoveryAtUtc >= MinTimeBetweenDailyRuns)
            .ToList();

        foreach (var user in due)
        {
            await queue.EnqueueDiscoveryAsync(new DiscoveryRequest(user.UserId, "daily"), ct);
        }

        var staleBefore = now - StaleQueuedAfter;
        var stale = await db.Applications
            .Where(a => (a.Status == ApplicationStatus.Queued || a.Status == ApplicationStatus.Preparing) && a.UpdatedAtUtc < staleBefore)
            .Select(a => a.Id)
            .Take(200)
            .ToListAsync(ct);

        foreach (var id in stale)
        {
            await queue.EnqueueApplyAsync(new ApplyRequest(id), ct);
        }

        logger.LogInformation("Scheduler: {Due} daily discoveries started, {Stale} stale applications re-queued", due.Count, stale.Count);
    }

    [Function(nameof(DiscoveryWorker))]
    public async Task DiscoveryWorker([QueueTrigger(JobQueues.Discovery)] DiscoveryRequest request, CancellationToken ct)
    {
        var outcome = await discovery.RunAsync(request.UserId, ct);
        logger.LogInformation("Discovery ({Trigger}) for {UserId}: {Summary}", request.Trigger, request.UserId, outcome.Summary);
    }

    [Function(nameof(ApplyWorker))]
    public Task ApplyWorker([QueueTrigger(JobQueues.Apply)] ApplyRequest request, CancellationToken ct) =>
        preparer.PrepareAsync(request.ApplicationId, ct);

    /// <summary>Retries exhausted: record the failure so the user sees it instead of a job stuck "in progress".</summary>
    [Function(nameof(ApplyPoisonHandler))]
    public Task ApplyPoisonHandler([QueueTrigger(JobQueues.Apply + "-poison")] ApplyRequest request, CancellationToken ct) =>
        preparer.MarkFailedAsync(request.ApplicationId, "Couldn't prepare this application after several attempts.", ct);

    private static int LocalHour(DateTime utcNow, string timeZoneId)
    {
        try
        {
            return TimeZoneInfo.ConvertTimeFromUtc(utcNow, TimeZoneInfo.FindSystemTimeZoneById(timeZoneId)).Hour;
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return utcNow.Hour;
        }
    }
}
