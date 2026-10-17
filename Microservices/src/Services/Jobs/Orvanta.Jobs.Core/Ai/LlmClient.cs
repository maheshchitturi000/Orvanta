using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Orvanta.Jobs.Core.Ai;

/// <summary>
/// Minimal LLM abstraction. Every caller has a non-AI fallback, so a missing or
/// slow model degrades quality but never breaks the workflow.
/// </summary>
public interface ILlmClient
{
    bool IsEnabled { get; }

    /// <summary>Returns the model's reply, or null if the model is unavailable.</summary>
    Task<string?> CompleteAsync(string system, string user, bool json, CancellationToken ct);
}

/// <summary>Ollama chat API (https://github.com/ollama/ollama/blob/main/docs/api.md#generate-a-chat-completion).</summary>
public sealed class OllamaLlmClient(HttpClient http, IOptions<JobsOptions> options, ILogger<OllamaLlmClient> logger) : ILlmClient
{
    // Circuit breaker shared by all instances: after a failure, skip the model for a while so
    // batches (e.g. 20 approved applications) fall back to templates instantly instead of each waiting.
    private static readonly TimeSpan BreakDuration = TimeSpan.FromMinutes(5);
    private static long _brokenUntilTicks;

    private readonly LlmOptions _options = options.Value.Llm;

    public bool IsEnabled => _options.Provider.Equals("ollama", StringComparison.OrdinalIgnoreCase);

    private static bool IsBroken => DateTime.UtcNow.Ticks < Interlocked.Read(ref _brokenUntilTicks);

    private void Break(string reason)
    {
        Interlocked.Exchange(ref _brokenUntilTicks, DateTime.UtcNow.Add(BreakDuration).Ticks);
        logger.LogWarning("LLM unavailable ({Reason}); using fallbacks for {Minutes} minutes", reason, BreakDuration.TotalMinutes);
    }

    public async Task<string?> CompleteAsync(string system, string user, bool json, CancellationToken ct)
    {
        if (!IsEnabled || IsBroken) return null;

        var body = new JsonObject
        {
            ["model"] = _options.Model,
            ["stream"] = false,
            ["think"] = false,
            ["options"] = new JsonObject { ["temperature"] = 0.2 },
            ["messages"] = new JsonArray(
                new JsonObject { ["role"] = "system", ["content"] = system },
                new JsonObject { ["role"] = "user", ["content"] = user })
        };
        if (json)
        {
            body["format"] = "json";
        }

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));

            using var response = await http.PostAsJsonAsync(new Uri(new Uri(_options.BaseUrl), "api/chat"), body, cts.Token);
            if (!response.IsSuccessStatusCode)
            {
                Break($"HTTP {(int)response.StatusCode}");
                return null;
            }

            var reply = await response.Content.ReadFromJsonAsync<JsonElement>(cts.Token);
            return reply.GetProperty("message").GetProperty("content").GetString();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException)
        {
            if (ct.IsCancellationRequested) return null; // caller gave up; not the model's fault
            Break(ex.Message);
            return null;
        }
    }
}
