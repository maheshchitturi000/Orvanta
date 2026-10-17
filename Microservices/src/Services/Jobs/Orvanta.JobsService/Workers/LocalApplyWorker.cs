using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Orvanta.Jobs.Core.Data;
using Orvanta.Jobs.Core.Domain;
using Orvanta.Jobs.Core.Workflow;

namespace Orvanta.JobsService.Workers;

/// <summary>
/// Safety net for the apply workflow. Azure Functions is the primary worker; when the
/// queue can't be reached (e.g. Azurite or Functions not running locally) approved
/// applications are prepared here instead, and applications left "Queued" for too long
/// are swept up periodically. <see cref="ApplicationPreparer"/> is idempotent, so a rare
/// overlap with the Functions worker is harmless.
/// </summary>
public sealed class LocalApplyWorker(IServiceScopeFactory scopes, IConfiguration configuration, ILogger<LocalApplyWorker> logger) : BackgroundService
{
    private static readonly TimeSpan SweepInterval = TimeSpan.FromMinutes(2);

    private readonly Channel<Guid> _work = Channel.CreateUnbounded<Guid>(new UnboundedChannelOptions { SingleReader = true });
    private readonly TimeSpan _staleAfter = TimeSpan.FromMinutes(configuration.GetValue("Jobs:LocalWorker:StaleMinutes", 10));
    private readonly bool _enabled = configuration.GetValue("Jobs:LocalWorker:Enabled", true);

    /// <summary>Prepare this application here (queue unavailable).</summary>
    public void Enqueue(Guid applicationId) => _work.Writer.TryWrite(applicationId);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_enabled)
        {
            logger.LogInformation("Local apply worker disabled; Azure Functions handles all applications");
            return;
        }

        var sweeper = SweepLoopAsync(stoppingToken);

        await foreach (var id in _work.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<ApplicationPreparer>().PrepareAsync(id, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Local preparation failed for application {ApplicationId}", id);
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<ApplicationPreparer>()
                    .MarkFailedAsync(id, "Couldn't prepare this application. Please try approving it again.", CancellationToken.None);
            }
        }

        await sweeper;
    }

    /// <summary>Picks up applications nobody processed (worker down when they were approved).</summary>
    private async Task SweepLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<JobsDbContext>();
                var staleBefore = DateTime.UtcNow - _staleAfter;
                var stale = await db.Applications.AsNoTracking()
                    .Where(a => (a.Status == ApplicationStatus.Queued || a.Status == ApplicationStatus.Preparing) && a.UpdatedAtUtc < staleBefore)
                    .OrderBy(a => a.ApprovedAtUtc)
                    .Select(a => a.Id)
                    .Take(50)
                    .ToListAsync(ct);

                if (stale.Count > 0)
                {
                    logger.LogInformation("Local apply worker picking up {Count} stale application(s)", stale.Count);
                    stale.ForEach(Enqueue);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Stale application sweep failed");
            }

            await Task.Delay(SweepInterval, ct);
        }
    }
}
