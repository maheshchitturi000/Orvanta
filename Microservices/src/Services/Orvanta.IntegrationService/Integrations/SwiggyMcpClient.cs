
using System.Diagnostics;
using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Orvanta.IntegrationService.Integrations;

public sealed class SwiggyMcpClient : ISwiggyMcpClient
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<SwiggyMcpClient> _logger;

    public SwiggyMcpClient(
        IConfiguration configuration,
        ILogger<SwiggyMcpClient> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SwiggyRemoteAddress>>
        GetAllAddressesAsync(
            string accessToken,
            CancellationToken cancellationToken = default)
    {
        var operationId = Guid.NewGuid();
        var stopwatch = Stopwatch.StartNew();

        if (string.IsNullOrWhiteSpace(accessToken))
        {
            throw new ArgumentException(
                "Swiggy access token is required.",
                nameof(accessToken));
        }

        var endpoint = _configuration["Swiggy:McpEndpoint"]
            ?? "https://mcp.swiggy.com/food";

        var addresses = new List<SwiggyRemoteAddress>();

        const int pageSize = 10;
        const int maxPages = 100;

        var page = 1;

        _logger.LogInformation(
            "Starting Swiggy address retrieval. OperationId: {OperationId}",
            operationId);

        try
        {
            var transport = new HttpClientTransport(
                new HttpClientTransportOptions
                {
                    Endpoint = new Uri(endpoint),
                    TransportMode = HttpTransportMode.StreamableHttp,
                    AdditionalHeaders = new Dictionary<string, string>
                    {
                        ["Authorization"] = $"Bearer {accessToken}"
                    }
                });

            await using var client = await McpClient.CreateAsync(
                transport,
                cancellationToken: cancellationToken);

            while (page <= maxPages)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var pageStopwatch = Stopwatch.StartNew();

                _logger.LogInformation(
                    "Calling Swiggy get_addresses. OperationId: {OperationId}, Page: {Page}",
                    operationId,
                    page);

                var result = await client.CallToolAsync(
                    "get_addresses",
                    new Dictionary<string, object?>
                    {
                        ["page"] = page,
                        ["pageSize"] = pageSize
                    },
                    cancellationToken: cancellationToken);

                if (result.IsError == true)
                {
                    var toolError = string.Join(
                        " ",
                        result.Content
                            .OfType<TextContentBlock>()
                            .Select(x => x.Text));

                    throw new InvalidOperationException(
                        $"Swiggy get_addresses failed on page {page}: {toolError}");
                }

                // Prefer structured content when the SDK exposes it.
                JsonElement root;
                var structuredContent = result.StructuredContent;

                if (structuredContent.HasValue)
                {
                    root = structuredContent.Value;
                }
                else
                {
                    var text = result.Content
                        .OfType<TextContentBlock>()
                        .Select(x => x.Text)
                        .FirstOrDefault(
                            x => !string.IsNullOrWhiteSpace(x));

                    if (string.IsNullOrWhiteSpace(text))
                    {
                        throw new InvalidOperationException(
                            $"Swiggy returned an empty response on page {page}.");
                    }

                    text = text.Trim();

                    // A formatted summary beginning with "Found..."
                    // is not JSON and cannot be parsed as address records.
                    if (!text.StartsWith("{", StringComparison.Ordinal))
                    {
                        _logger.LogError(
                            "Swiggy returned text instead of structured JSON. " +
                            "OperationId: {OperationId}, Page: {Page}, ResponseLength: {Length}",
                            operationId,
                            page,
                            text.Length);

                        throw new InvalidOperationException(
                            "Swiggy returned a human-readable address summary " +
                            "instead of structured address data. " +
                            "The response cannot safely be used for database synchronization.");
                    }

                    using var document = JsonDocument.Parse(text);
                    root = document.RootElement.Clone();
                }

                if (root.ValueKind != JsonValueKind.Object)
                {
                    throw new InvalidOperationException(
                        $"Unexpected Swiggy response type on page {page}.");
                }

                if (root.TryGetProperty("success", out var success) &&
                    success.ValueKind == JsonValueKind.False)
                {
                    var message =
                        root.TryGetProperty("error", out var error) &&
                        error.ValueKind == JsonValueKind.Object &&
                        error.TryGetProperty("message", out var errorMessage)
                            ? errorMessage.GetString()
                            : "Swiggy reported an address retrieval error.";

                    throw new InvalidOperationException(
                        message ?? "Swiggy address retrieval failed.");
                }

                if (!root.TryGetProperty("data", out var data) ||
                    data.ValueKind != JsonValueKind.Object ||
                    !data.TryGetProperty("addresses", out var items) ||
                    items.ValueKind != JsonValueKind.Array)
                {
                    throw new InvalidOperationException(
                        $"Swiggy response does not contain data.addresses on page {page}.");
                }

                var pageCount = 0;

                foreach (var item in items.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Object)
                        continue;

                    var id = GetString(item, "id");

                    if (string.IsNullOrWhiteSpace(id))
                    {
                        _logger.LogWarning(
                            "Skipping Swiggy address without an ID. Page: {Page}",
                            page);

                        continue;
                    }

                    addresses.Add(new SwiggyRemoteAddress(
                        id,
                        GetString(item, "addressLine"),
                        GetString(item, "phoneNumber"),
                        GetString(item, "addressCategory")
                            ?? GetString(item, "addressTag"),
                        item.GetRawText()));

                    pageCount++;
                }

                var hasMore =
                    data.TryGetProperty("pagination", out var pagination) &&
                    pagination.ValueKind == JsonValueKind.Object &&
                    pagination.TryGetProperty("hasMore", out var more) &&
                    more.ValueKind == JsonValueKind.True;

                pageStopwatch.Stop();

                _logger.LogInformation(
                    "Swiggy page processed. OperationId: {OperationId}, " +
                    "Page: {Page}, PageCount: {PageCount}, " +
                    "TotalAddresses: {TotalAddresses}, HasMore: {HasMore}, " +
                    "DurationMs: {DurationMs}",
                    operationId,
                    page,
                    pageCount,
                    addresses.Count,
                    hasMore,
                    pageStopwatch.ElapsedMilliseconds);

                if (!hasMore)
                {
                    stopwatch.Stop();

                    _logger.LogInformation(
                        "Swiggy address retrieval completed. OperationId: {OperationId}, " +
                        "TotalAddresses: {TotalAddresses}, TotalPages: {TotalPages}, " +
                        "DurationMs: {DurationMs}",
                        operationId,
                        addresses.Count,
                        page,
                        stopwatch.ElapsedMilliseconds);

                    return addresses;
                }

                page++;
            }

            throw new InvalidOperationException(
                $"Swiggy address pagination exceeded {maxPages} pages.");
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning(
                "Swiggy address retrieval cancelled. OperationId: {OperationId}, Page: {Page}",
                operationId,
                page);

            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Swiggy address retrieval failed. OperationId: {OperationId}, " +
                "Page: {Page}, AddressesRetrieved: {AddressCount}",
                operationId,
                page,
                addresses.Count);

            throw;
        }
    }

    private static string? GetString(
        JsonElement element,
        string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value) &&
               value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }
}
