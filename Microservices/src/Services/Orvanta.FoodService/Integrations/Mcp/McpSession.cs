using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Orvanta.FoodService.Integrations.Mcp;

/// <summary>Raised when the provider rejects the access token (HTTP 401/419 or JSON-RPC -32001).</summary>
public sealed class McpUnauthorizedException(string message) : Exception(message);

/// <summary>Raised for transport, protocol or tool-level failures.</summary>
public sealed class McpException(string message) : Exception(message);

/// <summary>
/// Minimal Model Context Protocol client over Streamable HTTP
/// (https://modelcontextprotocol.io/specification/2025-06-18/basic/transports).
/// One session = initialize → notifications/initialized → tools/call*.
/// </summary>
public sealed class McpSession
{
    private const string ProtocolVersion = "2025-06-18";

    private readonly HttpClient _http;
    private readonly string _endpoint;
    private readonly string _accessToken;
    private string? _sessionId;
    private int _nextId;

    private McpSession(HttpClient http, string endpoint, string accessToken)
    {
        _http = http;
        _endpoint = endpoint;
        _accessToken = accessToken;
    }

    public static async Task<McpSession> OpenAsync(HttpClient http, string endpoint, string accessToken, CancellationToken ct)
    {
        var session = new McpSession(http, endpoint, accessToken);

        await session.SendRequestAsync("initialize", new JsonObject
        {
            ["protocolVersion"] = ProtocolVersion,
            ["capabilities"] = new JsonObject(),
            ["clientInfo"] = new JsonObject { ["name"] = "orvanta-food-service", ["version"] = "1.0.0" }
        }, ct);

        await session.SendNotificationAsync("notifications/initialized", ct);
        return session;
    }

    /// <summary>
    /// Calls a tool and returns its payload: <c>structuredContent</c> when present,
    /// otherwise the first text block parsed as JSON (or wrapped as {"text": ...}).
    /// </summary>
    public async Task<JsonElement> CallToolAsync(string name, JsonObject arguments, CancellationToken ct)
    {
        var result = await SendRequestAsync("tools/call", new JsonObject { ["name"] = name, ["arguments"] = arguments }, ct);

        JsonElement payload;
        if (result.TryGetProperty("structuredContent", out var structured) && structured.ValueKind == JsonValueKind.Object)
        {
            payload = structured;
        }
        else
        {
            payload = ParseTextContent(result);
        }

        var isError = result.TryGetProperty("isError", out var err) && err.ValueKind == JsonValueKind.True;
        if (isError || IsEnvelopeFailure(payload))
        {
            throw new McpException(ExtractMessage(payload) ?? $"{name} failed.");
        }

        return payload;
    }

    private async Task<JsonElement> SendRequestAsync(string method, JsonObject parameters, CancellationToken ct)
    {
        var id = Interlocked.Increment(ref _nextId);
        var body = new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["method"] = method, ["params"] = parameters };

        using var response = await PostAsync(body, ct);
        if (response.Headers.TryGetValues("Mcp-Session-Id", out var sessionIds))
        {
            _sessionId ??= sessionIds.FirstOrDefault();
        }

        var message = await ReadResponseMessageAsync(response, id, ct);

        if (message.TryGetProperty("error", out var error))
        {
            var code = error.TryGetProperty("code", out var c) && c.TryGetInt32(out var n) ? n : 0;
            var text = error.TryGetProperty("message", out var m) ? m.GetString() : "MCP error";
            if (code == -32001)
            {
                throw new McpUnauthorizedException(text ?? "Unauthorized");
            }

            throw new McpException(text ?? "MCP error");
        }

        return message.TryGetProperty("result", out var result) ? result.Clone() : default;
    }

    private async Task SendNotificationAsync(string method, CancellationToken ct)
    {
        using var response = await PostAsync(new JsonObject { ["jsonrpc"] = "2.0", ["method"] = method }, ct);
    }

    private async Task<HttpResponseMessage> PostAsync(JsonObject body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, _endpoint)
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        request.Headers.Add("MCP-Protocol-Version", ProtocolVersion);
        if (_sessionId is not null)
        {
            request.Headers.Add("Mcp-Session-Id", _sessionId);
        }

        var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);

        if (response.StatusCode is HttpStatusCode.Unauthorized or (HttpStatusCode)419)
        {
            response.Dispose();
            throw new McpUnauthorizedException("The provider session has expired.");
        }

        if (response.StatusCode == HttpStatusCode.Forbidden)
        {
            response.Dispose();
            throw new McpUnauthorizedException("The provider denied access. Please reconnect with the required permissions.");
        }

        if (!response.IsSuccessStatusCode)
        {
            var status = (int)response.StatusCode;
            response.Dispose();
            throw new McpException($"Provider returned HTTP {status}.");
        }

        return response;
    }

    private static async Task<JsonElement> ReadResponseMessageAsync(HttpResponseMessage response, int id, CancellationToken ct)
    {
        var mediaType = response.Content.Headers.ContentType?.MediaType;

        if (mediaType == "text/event-stream")
        {
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var reader = new StreamReader(stream);
            var data = new StringBuilder();

            while (await reader.ReadLineAsync(ct) is { } line)
            {
                if (line.StartsWith("data:", StringComparison.Ordinal))
                {
                    data.Append(line.AsSpan(5).TrimStart());
                    continue;
                }

                if (line.Length == 0 && data.Length > 0)
                {
                    var message = TryMatch(data.ToString(), id);
                    data.Clear();
                    if (message is not null)
                    {
                        return message.Value;
                    }
                }
            }

            if (data.Length > 0 && TryMatch(data.ToString(), id) is { } last)
            {
                return last;
            }

            throw new McpException("The provider closed the stream without a response.");
        }

        var json = await response.Content.ReadAsStringAsync(ct);
        return TryMatch(json, id) ?? throw new McpException("The provider returned an unexpected response.");
    }

    private static JsonElement? TryMatch(string json, int id)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var messages = root.ValueKind == JsonValueKind.Array ? root.EnumerateArray().ToArray() : [root];

            foreach (var message in messages)
            {
                if (message.TryGetProperty("id", out var mid) && mid.ValueKind == JsonValueKind.Number && mid.GetInt32() == id)
                {
                    return message.Clone();
                }
            }
        }
        catch (JsonException)
        {
            // Not JSON (e.g. a keep-alive); ignore.
        }

        return null;
    }

    private static JsonElement ParseTextContent(JsonElement result)
    {
        if (result.ValueKind == JsonValueKind.Object && result.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
        {
            foreach (var block in content.EnumerateArray())
            {
                if (block.TryGetProperty("type", out var type) && type.GetString() == "text" && block.TryGetProperty("text", out var text))
                {
                    var value = text.GetString() ?? string.Empty;
                    try
                    {
                        using var doc = JsonDocument.Parse(value);
                        return doc.RootElement.Clone();
                    }
                    catch (JsonException)
                    {
                        using var wrapped = JsonDocument.Parse(new JsonObject { ["text"] = value }.ToJsonString());
                        return wrapped.RootElement.Clone();
                    }
                }
            }
        }

        return result;
    }

    private static bool IsEnvelopeFailure(JsonElement payload) =>
        payload.ValueKind == JsonValueKind.Object
        && payload.TryGetProperty("success", out var success)
        && success.ValueKind == JsonValueKind.False;

    private static string? ExtractMessage(JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (payload.TryGetProperty("error", out var error))
        {
            if (error.ValueKind == JsonValueKind.String) return error.GetString();
            if (error.ValueKind == JsonValueKind.Object && error.TryGetProperty("message", out var m)) return m.GetString();
        }

        if (payload.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.String) return message.GetString();
        if (payload.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String) return text.GetString();
        return null;
    }
}
