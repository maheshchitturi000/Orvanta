using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Orvanta.FoodService.Data;
using Orvanta.FoodService.Domain;
using Orvanta.FoodService.Options;
using Orvanta.Shared.Security;

namespace Orvanta.FoodService.Integrations.OAuth;

public sealed record AuthServerMetadata(string AuthorizationEndpoint, string TokenEndpoint, string? RegistrationEndpoint);

public sealed record TokenSet(string AccessToken, string? RefreshToken, int ExpiresInSeconds, string? Scope);

public sealed class OAuthException(string message) : Exception(message);

/// <summary>
/// OAuth 2.1 + PKCE client for MCP servers: RFC 8414 discovery,
/// RFC 7591 dynamic client registration, code exchange and refresh.
/// </summary>
public sealed class McpOAuthClient(
    IHttpClientFactory httpClientFactory,
    FoodDbContext db,
    AesGcmProtector protector,
    IOptions<FoodProvidersOptions> options,
    ILogger<McpOAuthClient> logger)
{
    private readonly FoodProvidersOptions _options = options.Value;

    public string RedirectUriFor(string provider) =>
        $"{_options.CallbackBaseUrl.TrimEnd('/')}/{provider.ToLowerInvariant()}/callback";

    public async Task<AuthServerMetadata> DiscoverAsync(ProviderSettings settings, CancellationToken ct)
    {
        var baseUrl = settings.AuthServerUrl.TrimEnd('/');
        var http = httpClientFactory.CreateClient("oauth");

        try
        {
            var doc = await http.GetFromJsonAsync<JsonElement>($"{baseUrl}/.well-known/oauth-authorization-server", ct);
            return new AuthServerMetadata(
                doc.GetProperty("authorization_endpoint").GetString()!,
                doc.GetProperty("token_endpoint").GetString()!,
                doc.TryGetProperty("registration_endpoint", out var reg) ? reg.GetString() : null);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            logger.LogWarning(ex, "OAuth metadata discovery failed for {Server}; using default paths", baseUrl);
            return new AuthServerMetadata($"{baseUrl}/auth/authorize", $"{baseUrl}/auth/token", $"{baseUrl}/auth/register");
        }
    }

    /// <summary>Returns a client id: configured, cached from DCR, or newly registered.</summary>
    public async Task<(string ClientId, string? ClientSecret)> GetClientAsync(
        string provider, ProviderSettings settings, AuthServerMetadata metadata, string redirectUri, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(settings.ClientId))
        {
            return (settings.ClientId, null);
        }

        var cached = await db.OAuthClientRegistrations.FindAsync([provider, redirectUri], ct);
        if (cached is not null)
        {
            return (cached.ClientId, cached.ClientSecretEncrypted is null ? null : protector.Unprotect(cached.ClientSecretEncrypted));
        }

        if (metadata.RegistrationEndpoint is null)
        {
            throw new OAuthException($"{settings.DisplayName} requires a pre-registered client id (FoodProviders:Providers:{provider}:ClientId).");
        }

        var http = httpClientFactory.CreateClient("oauth");
        using var response = await http.PostAsJsonAsync(metadata.RegistrationEndpoint, new
        {
            client_name = _options.ClientName,
            redirect_uris = new[] { redirectUri },
            grant_types = new[] { "authorization_code", "refresh_token" },
            response_types = new[] { "code" },
            token_endpoint_auth_method = "none",
            scope = settings.Scope
        }, ct);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            logger.LogError("Client registration with {Provider} failed: {Status} {Body}", provider, (int)response.StatusCode, body);
            throw new OAuthException($"Could not register Orvanta with {settings.DisplayName}. Your redirect URI may need to be allow-listed by the provider.");
        }

        var registration = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        var clientId = registration.GetProperty("client_id").GetString()!;
        var clientSecret = registration.TryGetProperty("client_secret", out var s) ? s.GetString() : null;

        db.OAuthClientRegistrations.Add(new OAuthClientRegistration
        {
            Provider = provider,
            RedirectUri = redirectUri,
            ClientId = clientId,
            ClientSecretEncrypted = clientSecret is null ? null : protector.Protect(clientSecret)
        });
        await db.SaveChangesAsync(ct);

        return (clientId, clientSecret);
    }

    /// <summary>Best-effort revocation of the provider session. Returns false if the provider refused or is unreachable.</summary>
    public async Task<bool> RevokeSessionAsync(string provider, ProviderSettings settings, string accessToken, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(settings.LogoutPath))
        {
            return false;
        }

        var url = $"{settings.AuthServerUrl.TrimEnd('/')}/{settings.LogoutPath.TrimStart('/')}";
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(new { }) };
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

            using var response = await httpClientFactory.CreateClient("oauth").SendAsync(request, ct);
            logger.LogInformation("Revoked {Provider} session: HTTP {Status}", provider, (int)response.StatusCode);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "Could not revoke {Provider} session", provider);
            return false;
        }
    }

    public static (string Verifier, string Challenge) CreatePkce()
    {
        var verifier = SecureHash.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var challenge = SecureHash.Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        return (verifier, challenge);
    }

    public Task<TokenSet> ExchangeCodeAsync(ProviderSettings settings, AuthServerMetadata metadata, string clientId, string? clientSecret,
        string code, string verifier, string redirectUri, CancellationToken ct) =>
        RequestTokenAsync(settings, metadata, new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["code_verifier"] = verifier,
            ["redirect_uri"] = redirectUri,
            ["client_id"] = clientId
        }, clientSecret, ct);

    public Task<TokenSet> RefreshAsync(ProviderSettings settings, AuthServerMetadata metadata, string clientId, string? clientSecret,
        string refreshToken, CancellationToken ct) =>
        RequestTokenAsync(settings, metadata, new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
            ["client_id"] = clientId
        }, clientSecret, ct);

    private async Task<TokenSet> RequestTokenAsync(ProviderSettings settings, AuthServerMetadata metadata,
        Dictionary<string, string> fields, string? clientSecret, CancellationToken ct)
    {
        if (clientSecret is not null)
        {
            fields["client_secret"] = clientSecret;
        }

        HttpContent content = settings.TokenRequestFormat.Equals("json", StringComparison.OrdinalIgnoreCase)
            ? JsonContent.Create(fields)
            : new FormUrlEncodedContent(fields);

        var http = httpClientFactory.CreateClient("oauth");
        using var response = await http.PostAsync(metadata.TokenEndpoint, content, ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Token request to {Provider} failed with {Status}", settings.DisplayName, (int)response.StatusCode);
            throw new OAuthException($"{settings.DisplayName} rejected the authorization. Please connect again.");
        }

        var token = JsonSerializer.Deserialize<TokenResponse>(body)
                    ?? throw new OAuthException($"{settings.DisplayName} returned an empty token response.");

        if (string.IsNullOrEmpty(token.AccessToken))
        {
            throw new OAuthException($"{settings.DisplayName} did not return an access token.");
        }

        return new TokenSet(token.AccessToken, token.RefreshToken, token.ExpiresIn ?? 3600, token.Scope);
    }

    private sealed class TokenResponse
    {
        [JsonPropertyName("access_token")] public string? AccessToken { get; set; }
        [JsonPropertyName("refresh_token")] public string? RefreshToken { get; set; }
        [JsonPropertyName("expires_in")] public int? ExpiresIn { get; set; }
        [JsonPropertyName("scope")] public string? Scope { get; set; }
    }
}
