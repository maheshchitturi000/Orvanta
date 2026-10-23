using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orvanta.Jobs.Core.Data;
using Orvanta.Jobs.Core.Domain;
using Orvanta.Jobs.Core.Matching;
using Orvanta.Jobs.Core.Sources;

namespace Orvanta.Jobs.Core.Workflow;

/// <summary>A job that scored below the user's bar, shown so "0 matches" is explainable.</summary>
public sealed record NearMiss(SourcedJob Job, MatchResult Match);

public sealed record DiscoveryOutcome(int BoardsScanned, int JobsScanned, int NewMatches, IReadOnlyList<string> Errors, int SearchesRun = 0)
{
    public IReadOnlyList<NearMiss> NearMisses { get; init; } = [];

    /// <summary>False when no job search source (JSearch, Adzuna) is connected for the user.</summary>
    public bool SearchConnected { get; init; }

    /// <summary>The roles searched for (chosen by the user, or derived from the CV).</summary>
    public IReadOnlyList<string> RolesUsed { get; init; } = [];

    public string Summary =>
        $"Scanned {JobsScanned} jobs on {BoardsScanned} boards" +
        (SearchesRun > 0 ? $" + {SearchesRun} job site search{(SearchesRun == 1 ? "" : "es")}" : string.Empty) +
        $" · {NewMatches} new match{(NewMatches == 1 ? "" : "es")}" +
        (Errors.Count > 0 ? $" · {Errors.Count} source(s) unavailable" : string.Empty);
}

/// <summary>
/// Finds new jobs for one user: fetch boards → filter → score → store matches above the user's threshold.
/// Idempotent: jobs already matched for the user are skipped, so retries and daily runs never duplicate.
/// </summary>
public sealed class DiscoveryWorkflow(
    JobsDbContext db,
    JobSourceRegistry sources,
    IEnumerable<IJobSearchSource> searchSources,
    JobSourceCredentials credentials,
    IMemoryCache cache,
    IOptions<JobsOptions> options,
    ILogger<DiscoveryWorkflow> logger)
{
    private static readonly TimeSpan BoardCacheDuration = TimeSpan.FromMinutes(30);
    private const int ParallelBoards = 6;
    private const int ParallelSearches = 3; // gentle on small API quotas and rate limits
    private const int NearMissFloor = 50;
    private const int NearMissCount = 5;

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

        var errors = new ConcurrentQueue<string>();

        // Boards and searches are independent web requests: run them side by side, so a run takes about as
        // long as its slowest source instead of the sum of all of them. Results keep their order (boards first).
        // Keys come from the database first (DbContext isn't thread-safe); everything after is plain web requests.
        var queries = BuildQueries(profile, preference);
        var searches = new List<(IJobSearchSource Source, JobSearchQuery Query, string ApiKey)>();
        foreach (var search in searchSources)
        {
            var apiKey = await credentials.GetKeyAsync(userId, search.Name, ct);
            if (apiKey is null) continue;
            searches.AddRange(queries.Select(q => (search, q, apiKey)));
        }

        var boardResults = new IReadOnlyList<SourcedJob>[boards.Count];
        var searchResults = new IReadOnlyList<SourcedJob>[searches.Count];
        await Task.WhenAll(
            Parallel.ForEachAsync(Enumerable.Range(0, boards.Count),
                new ParallelOptions { MaxDegreeOfParallelism = ParallelBoards, CancellationToken = ct },
                async (i, token) => boardResults[i] = await FetchBoardAsync(boards[i], errors, token)),
            Parallel.ForEachAsync(Enumerable.Range(0, searches.Count),
                new ParallelOptions { MaxDegreeOfParallelism = ParallelSearches, CancellationToken = ct },
                async (i, token) => searchResults[i] = await SearchAsync(searches[i].Source, searches[i].Query, searches[i].ApiKey, errors, token)));

        var jobs = boardResults.Concat(searchResults).SelectMany(r => r).ToList();
        var searchesRun = searches.Count;
        var searchConnected = searches.Count > 0;

        // The same job often appears on its company board and on LinkedIn/Indeed; keep the first
        // (boards come first, and their direct ATS links are the easiest to fill).
        var unique = jobs.DistinctBy(DuplicateKey).ToList();

        var scored = await ScoreAndSaveAsync(userId, profile, preference, unique, ct);
        var outcome = new DiscoveryOutcome(boards.Count, unique.Count, scored.NewMatches, errors.ToList(), searchesRun)
        {
            NearMisses = scored.NearMisses,
            SearchConnected = searchConnected,
            RolesUsed = RoleSuggester.EffectiveRoles(profile, preference)
        };

        preference.LastDiscoveryAtUtc = DateTime.UtcNow;
        preference.LastDiscoverySummary = outcome.Summary.Length > 500 ? outcome.Summary[..500] : outcome.Summary;
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Discovery for {UserId}: {Summary}", userId, outcome.Summary);
        return outcome;
    }

    /// <summary>Filters (age, location, company), scores, and stores matches at or above the user's bar.</summary>
    private async Task<(int NewMatches, IReadOnlyList<NearMiss> NearMisses)> ScoreAndSaveAsync(
        Guid userId, CandidateProfile profile, JobPreference preference, IReadOnlyList<SourcedJob> jobs, CancellationToken ct)
    {
        var threshold = Threshold(preference);
        var oldest = DateTime.UtcNow.AddDays(-Math.Max(preference.MaxPostingAgeDays, 1));
        var candidates = new List<(SourcedJob Job, MatchResult Match)>();
        var belowBar = new List<NearMiss>();
        foreach (var job in jobs)
        {
            if (job.PostedAtUtc is { } posted && posted < oldest) continue;
            if (!JobMatcher.PassesFilters(preference, job)) continue;

            var match = JobMatcher.Score(profile, preference, job);
            if (match.Score >= threshold)
            {
                candidates.Add((job, match));
            }
            else if (match.Score >= NearMissFloor)
            {
                belowBar.Add(new NearMiss(job, match));
            }
        }

        var newMatches = await SaveMatchesAsync(userId, candidates.OrderByDescending(c => c.Match.Score).ToList(), ct);
        return (newMatches, belowBar.OrderByDescending(n => n.Match.Score).Take(NearMissCount).ToList());
    }

    private int Threshold(JobPreference preference) =>
        Math.Clamp(preference.MinMatchScore > 0 ? preference.MinMatchScore : _options.DefaultMatchThreshold, 50, 100);

    /// <summary>
    /// Target roles × locations, most important first, limited by the per-run quota.
    /// No locations → one query per role; "Remote only" → remote queries.
    /// </summary>
    public IReadOnlyList<JobSearchQuery> BuildQueries(CandidateProfile profile, JobPreference preference)
    {
        var roles = RoleSuggester.EffectiveRoles(profile, preference);

        IEnumerable<JobSearchQuery> queries = preference.RemoteOnly || preference.Locations.Count == 0
            ? roles.Select(r => new JobSearchQuery(r, null, preference.RemoteOnly))
            // Interleave so a small quota covers different roles before repeating locations.
            : preference.Locations.SelectMany((location, li) => roles.Select((role, ri) => (Query: new JobSearchQuery(role, location, false), Order: li * 100 + ri)))
                .OrderBy(x => x.Order)
                .Select(x => x.Query);

        return queries.Take(Math.Max(_options.JSearch.MaxQueriesPerRun, 0)).ToList();
    }

    private async Task<IReadOnlyList<SourcedJob>> SearchAsync(IJobSearchSource source, JobSearchQuery query, string apiKey, ConcurrentQueue<string> errors, CancellationToken ct)
    {
        // Search quotas are small (≈200/month on free plans): reuse results for a few hours.
        var key = $"search:{source.Name}:{query.Role}|{query.Location}|{query.RemoteOnly}".ToLowerInvariant();
        if (cache.TryGetValue<IReadOnlyList<SourcedJob>>(key, out var cached) && cached is not null)
        {
            return cached;
        }

        try
        {
            var jobs = await source.SearchAsync(query, apiKey, ct);
            cache.Set(key, jobs, TimeSpan.FromHours(Math.Max(_options.JSearch.CacheHours, 1)));
            return jobs;
        }
        // A cancelled run (time limit, user left) stops; only a failing site is reported and skipped.
        catch (Exception ex) when ((ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException) && !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "{Source} search \"{Role}\" in {Location} failed", source.Name, query.Role, query.Location);
            errors.Enqueue($"{source.Name}: {ex.Message}");
            return [];
        }
    }

    private static string DuplicateKey(SourcedJob job) =>
        string.Join("|", Normalize(job.Company), Normalize(job.Title), Normalize(job.Location.Split(',')[0]));

    private static string Normalize(string value) =>
        new string(value.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

    private async Task<IReadOnlyList<SourcedJob>> FetchBoardAsync(CompanyBoard board, ConcurrentQueue<string> errors, CancellationToken ct)
    {
        var source = sources.Get(board.Provider);
        if (source is null)
        {
            errors.Enqueue($"{board}: unsupported provider");
            return [];
        }

        // Many users share the same boards; cache each board briefly so a run costs one request per board.
        var key = $"board:{board}".ToLowerInvariant();
        if (cache.TryGetValue<IReadOnlyList<SourcedJob>>(key, out var cached) && cached is not null)
        {
            return cached;
        }

        // A slow board is skipped for this run rather than holding up the whole search.
        using var boardTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        boardTimeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(_options.BoardTimeoutSeconds, 1)));
        try
        {
            var jobs = await source.FetchAsync(board.Board, boardTimeout.Token);
            cache.Set(key, jobs, BoardCacheDuration);
            return jobs;
        }
        catch (Exception ex) when ((ex is HttpRequestException or OperationCanceledException or System.Text.Json.JsonException or KeyNotFoundException)
                                   && !ct.IsCancellationRequested)
        {
            var timedOut = ex is OperationCanceledException;
            logger.LogWarning(timedOut ? null : ex, "Board {Board} unavailable{Reason}", board, timedOut ? $" (slower than {_options.BoardTimeoutSeconds}s)" : string.Empty);
            errors.Enqueue(timedOut ? $"{board}: too slow, skipped" : $"{board}: unavailable");
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
