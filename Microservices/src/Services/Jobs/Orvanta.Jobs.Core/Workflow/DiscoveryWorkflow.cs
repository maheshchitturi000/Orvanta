using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orvanta.Jobs.Core.Data;
using Orvanta.Jobs.Core.Domain;
using Orvanta.Jobs.Core.Matching;
using Orvanta.Jobs.Core.Sources;

namespace Orvanta.Jobs.Core.Workflow;

public sealed record DiscoveryOutcome(int BoardsScanned, int JobsScanned, int NewMatches, IReadOnlyList<string> Errors)
{
    public string Summary =>
        $"Scanned {JobsScanned} jobs on {BoardsScanned} boards · {NewMatches} new match{(NewMatches == 1 ? "" : "es")}" +
        (Errors.Count > 0 ? $" · {Errors.Count} board(s) unavailable" : string.Empty);
}

/// <summary>
/// Finds new jobs for one user: fetch boards → filter → score → store matches above the user's threshold.
/// Idempotent: jobs already matched for the user are skipped, so retries and daily runs never duplicate.
/// </summary>
public sealed class DiscoveryWorkflow(
    JobsDbContext db,
    JobSourceRegistry sources,
    IMemoryCache cache,
    IOptions<JobsOptions> options,
    ILogger<DiscoveryWorkflow> logger)
{
    private static readonly TimeSpan BoardCacheDuration = TimeSpan.FromMinutes(30);
    private readonly JobsOptions _options = options.Value;

    public async Task<DiscoveryOutcome> RunAsync(Guid userId, CancellationToken ct)
    {
        var profile = await db.Profiles.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == userId, ct);
        var preference = await db.Preferences.FirstOrDefaultAsync(p => p.UserId == userId, ct);
        if (profile is null || preference is null)
        {
            return new DiscoveryOutcome(0, 0, 0, ["Upload your CV and save your preferences first."]);
        }

        var boards = (preference.CompanyBoards.Count > 0 ? preference.CompanyBoards : _options.DefaultBoards)
            .Select(b => CompanyBoard.TryParse(b, out var board) ? board : null)
            .OfType<CompanyBoard>()
            .DistinctBy(b => b.ToString(), StringComparer.OrdinalIgnoreCase)
            .ToList();

        var threshold = Math.Clamp(preference.MinMatchScore > 0 ? preference.MinMatchScore : _options.DefaultMatchThreshold, 50, 100);
        var oldest = DateTime.UtcNow.AddDays(-Math.Max(preference.MaxPostingAgeDays, 1));
        var errors = new List<string>();
        var candidates = new List<(SourcedJob Job, MatchResult Match)>();
        var scanned = 0;

        foreach (var board in boards)
        {
            var jobs = await FetchBoardAsync(board, errors, ct);
            scanned += jobs.Count;

            foreach (var job in jobs)
            {
                if (job.PostedAtUtc is { } posted && posted < oldest) continue;
                if (!JobMatcher.PassesFilters(preference, job)) continue;

                var match = JobMatcher.Score(profile, preference, job);
                if (match.Score >= threshold)
                {
                    candidates.Add((job, match));
                }
            }
        }

        var newMatches = await SaveMatchesAsync(userId, candidates.OrderByDescending(c => c.Match.Score).ToList(), ct);
        var outcome = new DiscoveryOutcome(boards.Count, scanned, newMatches, errors);

        preference.LastDiscoveryAtUtc = DateTime.UtcNow;
        preference.LastDiscoverySummary = outcome.Summary.Length > 500 ? outcome.Summary[..500] : outcome.Summary;
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Discovery for {UserId}: {Summary}", userId, outcome.Summary);
        return outcome;
    }

    private async Task<IReadOnlyList<SourcedJob>> FetchBoardAsync(CompanyBoard board, List<string> errors, CancellationToken ct)
    {
        var source = sources.Get(board.Provider);
        if (source is null)
        {
            errors.Add($"{board}: unsupported provider");
            return [];
        }

        // Many users share the same boards; cache each board briefly so a run costs one request per board.
        var key = $"board:{board}".ToLowerInvariant();
        if (cache.TryGetValue<IReadOnlyList<SourcedJob>>(key, out var cached) && cached is not null)
        {
            return cached;
        }

        try
        {
            var jobs = await source.FetchAsync(board.Board, ct);
            cache.Set(key, jobs, BoardCacheDuration);
            return jobs;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException or KeyNotFoundException)
        {
            logger.LogWarning(ex, "Board {Board} unavailable", board);
            errors.Add($"{board}: unavailable");
            return [];
        }
    }

    private async Task<int> SaveMatchesAsync(Guid userId, IReadOnlyList<(SourcedJob Job, MatchResult Match)> candidates, CancellationToken ct)
    {
        if (candidates.Count == 0) return 0;

        var keys = candidates.Select(c => c.Job.Source + "|" + c.Job.ExternalId).ToHashSet();
        var existingPostings = await db.Postings
            .Where(p => keys.Contains(p.Source + "|" + p.ExternalId))
            .ToDictionaryAsync(p => p.Source + "|" + p.ExternalId, ct);

        var existingIds = existingPostings.Values.Select(p => p.Id).ToList();
        var alreadyMatched = (await db.Matches
            .Where(m => m.UserId == userId && existingIds.Contains(m.JobPostingId))
            .Select(m => m.JobPostingId)
            .ToListAsync(ct))
            .ToHashSet();

        var added = 0;
        foreach (var (job, match) in candidates)
        {
            if (added >= _options.MaxNewMatchesPerRun) break;

            var key = job.Source + "|" + job.ExternalId;
            if (!existingPostings.TryGetValue(key, out var posting))
            {
                posting = new JobPosting
                {
                    Source = job.Source,
                    ExternalId = job.ExternalId,
                    Company = Trim(job.Company, 150),
                    Title = Trim(job.Title, 300),
                    Location = Trim(job.Location, 300),
                    IsRemote = job.IsRemote,
                    Url = Trim(job.Url, 500),
                    ApplyUrl = Trim(job.ApplyUrl, 500),
                    Description = job.Description,
                    PostedAtUtc = job.PostedAtUtc
                };
                db.Postings.Add(posting);
                existingPostings[key] = posting;
            }
            else if (alreadyMatched.Contains(posting.Id))
            {
                continue;
            }

            db.Matches.Add(new JobMatch
            {
                UserId = userId,
                JobPostingId = posting.Id,
                Score = match.Score,
                MatchedSkills = match.MatchedSkills.ToList(),
                MissingSkills = match.MissingSkills.ToList(),
                Reasons = match.Reasons.ToList()
            });
            added++;
        }

        await db.SaveChangesAsync(ct);
        return added;
    }

    private static string Trim(string value, int max) => value.Length <= max ? value : value[..max];
}
