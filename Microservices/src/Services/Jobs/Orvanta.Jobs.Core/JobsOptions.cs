namespace Orvanta.Jobs.Core;

public sealed class JobsOptions
{
    public const string SectionName = "Jobs";

    /// <summary>Azure Storage connection string. "UseDevelopmentStorage=true" uses Azurite locally.</summary>
    public string QueueConnectionString { get; set; } = "UseDevelopmentStorage=true";

    public int DefaultMatchThreshold { get; set; } = 80;
    public int MaxNewMatchesPerRun { get; set; } = 40;

    /// <summary>Boards scanned when the user hasn't chosen any, as "provider:board".</summary>
    public List<string> DefaultBoards { get; set; } = [];

    /// <summary>Base64 32-byte key that encrypts users' source credentials. Same value for the API and the Functions.</summary>
    public string SecretsKey { get; set; } = string.Empty;

    public LlmOptions Llm { get; set; } = new();

    public JSearchOptions JSearch { get; set; } = new();
}

/// <summary>
/// JSearch (Google for Jobs data via RapidAPI): listings published on LinkedIn, Naukri, Indeed,
/// Glassdoor and company sites, in any country. Key goes in user-secrets: Jobs:JSearch:ApiKey.
/// </summary>
public sealed class JSearchOptions
{
    public bool Enabled { get; set; } = true;
    public string ApiKey { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = "https://jsearch.p.rapidapi.com";
    public string Host { get; set; } = "jsearch.p.rapidapi.com";

    /// <summary>Search endpoint path. JSearch replaced "/search" with "/search-v2".</summary>
    public string SearchPath { get; set; } = "/search-v2";

    /// <summary>Searches per discovery run (role × location). Each costs one API request.</summary>
    public int MaxQueriesPerRun { get; set; } = 3;

    /// <summary>Pages of 10 results per search.</summary>
    public int PagesPerQuery { get; set; } = 1;

    /// <summary>all | today | 3days | week | month</summary>
    public string DatePosted { get; set; } = "week";

    public int CacheHours { get; set; } = 6;

    public bool IsConfigured => Enabled && !string.IsNullOrWhiteSpace(ApiKey);
}

public sealed class LlmOptions
{
    /// <summary>"ollama" (local) or "none" (heuristics/templates only).</summary>
    public string Provider { get; set; } = "ollama";
    public string BaseUrl { get; set; } = "http://localhost:11434/";
    public string Model { get; set; } = "qwen3:4b";
    public int TimeoutSeconds { get; set; } = 120;
}

/// <summary>Queue names shared by the API (producer) and the Functions (consumers).</summary>
public static class JobQueues
{
    public const string Discovery = "job-discovery";
    public const string Apply = "job-apply";
}

public sealed record DiscoveryRequest(Guid UserId, string Trigger);

public sealed record ApplyRequest(Guid ApplicationId);
