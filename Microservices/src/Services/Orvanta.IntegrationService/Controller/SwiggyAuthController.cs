
using System.Security.Cryptography;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Orvanta.IntegrationService.Repositories;
using Orvanta.IntegrationService.Integrations;

namespace Orvanta.IntegrationService.Controllers;

[ApiController]
[Route("api/integrations/swiggy")]
public sealed class SwiggyAuthController : ControllerBase
{
    private const string Provider = "swiggy";

    private readonly IConfiguration _configuration;
    private readonly IMemoryCache _cache;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IExternalIntegrationRepository _repository;
    private readonly ISwiggyMcpClient _swiggyMcpClient;
    private readonly ILogger<SwiggyAuthController> _logger;

    public SwiggyAuthController(
        IConfiguration configuration,
        IMemoryCache cache,
        IHttpClientFactory httpClientFactory,
        IExternalIntegrationRepository repository,
        ISwiggyMcpClient swiggyMcpClient,
        ILogger<SwiggyAuthController> logger)
    {
        _configuration = configuration;
        _cache = cache;
        _httpClientFactory = httpClientFactory;
        _repository = repository;
        _swiggyMcpClient = swiggyMcpClient;
        _logger = logger;
    }

    [HttpPost("connect")]
    public IActionResult Connect()
    {
        var clientId = _configuration["Swiggy:ClientId"];
        var redirectUri = _configuration["Swiggy:RedirectUri"];

        if (string.IsNullOrWhiteSpace(clientId) ||
            string.IsNullOrWhiteSpace(redirectUri))
        {
            return Problem("Swiggy OAuth configuration is missing.",
                statusCode: 500);
        }

        var state = CreateRandomValue();
        var verifier = CreateRandomValue();

        var challenge = Convert.ToBase64String(
                SHA256.HashData(Encoding.ASCII.GetBytes(verifier)))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

        _cache.Set($"swiggy-oauth:{state}", verifier,
            TimeSpan.FromMinutes(10));

        var parameters = new Dictionary<string, string>
        {
            ["response_type"] = "code",
            ["client_id"] = clientId,
            ["redirect_uri"] = redirectUri,
            ["state"] = state,
            ["scope"] = "mcp:tools",
            ["code_challenge"] = challenge,
            ["code_challenge_method"] = "S256"
        };

        var url = "https://mcp.swiggy.com/auth/authorize?" +
            string.Join("&", parameters.Select(p =>
                $"{Uri.EscapeDataString(p.Key)}=" +
                $"{Uri.EscapeDataString(p.Value)}"));

        return Ok(new { authorizationUrl = url });
    }

    [HttpGet("callback")]
    public async Task<IActionResult> Callback(
        [FromQuery] string? code,
        [FromQuery] string? state,
        [FromQuery] string? error,
        CancellationToken cancellationToken)
    {
        var frontendUrl = _configuration["Swiggy:FrontendReturnUrl"]
            ?? "http://localhost:4200/integrations";

        if (!string.IsNullOrWhiteSpace(error))
            return Redirect(AddQuery(frontendUrl, "swiggy", "error"));

        if (string.IsNullOrWhiteSpace(code) ||
            string.IsNullOrWhiteSpace(state))
            return BadRequest("Missing OAuth code or state.");

        var cacheKey = $"swiggy-oauth:{state}";

        if (!_cache.TryGetValue(cacheKey, out string? verifier) ||
            string.IsNullOrWhiteSpace(verifier))
            return BadRequest("OAuth state expired. Please reconnect.");

        _cache.Remove(cacheKey);

        var clientId = _configuration["Swiggy:ClientId"];
        var redirectUri = _configuration["Swiggy:RedirectUri"];

        if (string.IsNullOrWhiteSpace(clientId) ||
            string.IsNullOrWhiteSpace(redirectUri))
            return Problem("Swiggy OAuth configuration is missing.",
                statusCode: 500);

        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Post, "https://mcp.swiggy.com/auth/token");

            request.Content = new FormUrlEncodedContent(
                new Dictionary<string, string>
                {
                    ["grant_type"] = "authorization_code",
                    ["client_id"] = clientId,
                    ["code"] = code,
                    ["redirect_uri"] = redirectUri,
                    ["code_verifier"] = verifier
                });

            using var response = await _httpClientFactory
                .CreateClient()
                .SendAsync(request, cancellationToken);

            var tokenJson = await response.Content
                .ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Swiggy token exchange failed with HTTP {StatusCode}.",
                    (int)response.StatusCode);

                return Redirect(AddQuery(frontendUrl, "swiggy", "error"));
            }

            var saved = await _repository.SaveIntegrationAsync(
                "Swiggy", Provider, true, tokenJson, cancellationToken);

            if (!saved)
                return Problem("Could not save Swiggy credentials.",
                    statusCode: 500);

            return Redirect(AddQuery(frontendUrl, "swiggy", "authorized"));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Swiggy token endpoint request failed.");
            return Redirect(AddQuery(frontendUrl, "swiggy", "error"));
        }
    }

    [HttpGet("status")]
    public async Task<IActionResult> Status(
        CancellationToken cancellationToken)
    {
        var status = await _repository.GetIntegrationStatusAsync(
            Provider, cancellationToken);

        return Ok(status ?? new IntegrationStatusDto(
            "Swiggy", Provider, false, null));
    }

    [HttpGet("saved-addresses")]
    public async Task<IActionResult> GetSavedAddresses(
        CancellationToken cancellationToken)
    {
        var userId = GetUserId();
        //if (userId is null)
        //    return Unauthorized("Authenticated user identity is required.");

        var addresses = await _repository.GetSavedAddressesAsync(
            Provider, userId, cancellationToken);

        return Ok(new { source = "database", addresses });
    }

    [HttpGet("addresses")]
    public async Task<IActionResult> GetAddresses(
        [FromQuery] bool refresh = false,
        CancellationToken cancellationToken = default)
    {
        var userId = GetUserId();
        //if (userId is null)
        //    return Unauthorized("Authenticated user identity is required.");

        try
        {
            if (!refresh)
            {
                var saved = (await _repository.GetSavedAddressesAsync(
                    Provider, userId, cancellationToken)).ToList();

                if (saved.Count > 0)
                    return Ok(new { source = "database", addresses = saved });
            }

            var integration = await _repository.GetIntegrationAsync(
                Provider, cancellationToken);

            if (integration is null || !integration.IsConnected ||
                string.IsNullOrWhiteSpace(integration.IntegrationDataJson))
            {
                return Conflict(new
                {
                    message = "Connect Swiggy before retrieving addresses."
                });
            }

            var accessToken = TokenHelper.ReadAccessToken(
                integration.IntegrationDataJson);

            if (string.IsNullOrWhiteSpace(accessToken))
                return Conflict(new
                {
                    message = "The Swiggy access token is missing. Reconnect Swiggy."
                });

            var remoteAddresses = await _swiggyMcpClient.GetAllAddressesAsync(
                accessToken, cancellationToken);

            foreach (var address in remoteAddresses)
            {
                await _repository.SaveAddressAsync(
                    Provider,
                    address.Id,
                    userId,
                    address.Label,
                    address.PhoneNumber,
                    address.AddressLine,
                    false,
                    address.RawJson,
                    cancellationToken);
            }

            var updated = await _repository.GetSavedAddressesAsync(
                Provider, userId, cancellationToken);

            return Ok(new { source = "swiggy", addresses = updated });
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve Swiggy addresses.");

            return Problem(
                "Could not retrieve Swiggy addresses. Check the service logs.",
                statusCode: 502);
        }
    }

    private string? GetUserId() =>
        User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

    private static string CreateRandomValue() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string AddQuery(string url, string key, string value) =>
        url + (url.Contains('?') ? "&" : "?") +
        Uri.EscapeDataString(key) + "=" + Uri.EscapeDataString(value);
}

internal static class TokenHelper
{
    public static string? ReadAccessToken(string json)
    {
        using var document = System.Text.Json.JsonDocument.Parse(json);

        return document.RootElement.TryGetProperty(
            "access_token", out var token)
            ? token.GetString()
            : null;
    }
}
