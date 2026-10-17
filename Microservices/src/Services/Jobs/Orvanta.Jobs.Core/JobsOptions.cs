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

    public LlmOptions Llm { get; set; } = new();
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
