using System.Net.Http.Headers;
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

/// <summary>
/// Chat completion against either Ollama ("ollama", https://github.com/ollama/ollama/blob/main/docs/api.md#generate-a-chat-completion)
/// or any OpenAI-compatible API ("openai": Groq, Google Gemini, OpenRouter...).
/// </summary>
public sealed class LlmClient(HttpClient http, IOptions<JobsOptions> options, ILogger<LlmClient> logger) : ILlmClient
{
    // Circuit breaker shared by all instances: after a failure, skip the model for a while so
    // batches (e.g. 20 approved applications) fall back to templates instantly instead of each waiting.
    private static readonly TimeSpan BreakDuration = TimeSpan.FromMinutes(5);
    private static long _brokenUntilTicks;

    private readonly LlmOptions _options = options.Value.Llm;

    private bool IsOpenAi => _options.Provider.Equals("openai", StringComparison.OrdinalIgnoreCase);

    public bool IsEnabled => IsOpenAi || _options.Provider.Equals("ollama", StringComparison.OrdinalIgnoreCase);

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
            ["messages"] = new JsonArray(
                new JsonObject { ["role"] = "system", ["content"] = system },
                new JsonObject { ["role"] = "user", ["content"] = user })
        };
        if (IsOpenAi)
        {
            body["temperature"] = 0.2;
            if (json) body["response_format"] = new JsonObject { ["type"] = "json_object" };
        }
        else
        {
            body["think"] = false;
            body["options"] = new JsonObject { ["temperature"] = 0.2 };
            if (json) body["format"] = "json";
        }

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));

            var baseUri = new Uri(_options.BaseUrl.TrimEnd('/') + "/");
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(baseUri, IsOpenAi ? "chat/completions" : "api/chat"))
            {
                Content = JsonContent.Create(body)
            };
            if (!string.IsNullOrWhiteSpace(_options.ApiKey))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
            }

            using var response = await http.SendAsync(request, cts.Token);
            if (!response.IsSuccessStatusCode)
            {
                Break($"HTTP {(int)response.StatusCode}");
                return null;
            }

            var reply = await response.Content.ReadFromJsonAsync<JsonElement>(cts.Token);
            var message = IsOpenAi ? reply.GetProperty("choices")[0].GetProperty("message") : reply.GetProperty("message");
            return message.GetProperty("content").GetString();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException)
        {
            if (ct.IsCancellationRequested) return null; // caller gave up; not the model's fault
            Break(ex.Message);
            return null;
        }
    }
}
