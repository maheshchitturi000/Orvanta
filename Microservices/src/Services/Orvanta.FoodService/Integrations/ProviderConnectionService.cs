using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Orvanta.FoodService.Data;
using Orvanta.FoodService.Domain;
using Orvanta.FoodService.Integrations.Mcp;
using Orvanta.FoodService.Integrations.OAuth;
using Orvanta.FoodService.Options;
using Orvanta.Shared.Responses;
using Orvanta.Shared.Security;

namespace Orvanta.FoodService.Integrations;

public sealed record ProviderStatus(
    string Provider, string DisplayName, string Category, bool Enabled, bool IsConnected, DateTime? ConnectedAt);

public sealed record ConnectResult(string AuthorizationUrl);

/// <summary>Thrown when a provider is not connected or its connection can no longer be refreshed.</summary>
public sealed class ProviderNotConnectedException(string provider, string message) : Exception(message)
{
    public string Provider { get; } = provider;
}

/// <summary>
/// Owns everything about a user's link to a provider: OAuth start/callback,
/// encrypted token storage, refresh, and opening authorised MCP sessions.
/// </summary>
public sealed class ProviderConnectionService(
    FoodDbContext db,
    McpOAuthClient oauth,
    AesGcmProtector protector,
    IHttpClientFactory httpClientFactory,
    IOptions<FoodProvidersOptions> options,
    ILogger<ProviderConnectionService> logger)
{
    private static readonly TimeSpan RefreshSkew = TimeSpan.FromSeconds(60);
    private readonly FoodProvidersOptions _options = options.Value;

    public IReadOnlyList<string> EnabledProviders =>
        _options.Providers.Where(p => p.Value.Enabled).Select(p => p.Key.ToLowerInvariant()).ToList();

    public ProviderSettings? Settings(string provider) =>
        _options.Providers.TryGetValue(provider, out var s) && s.Enabled ? s : null;

    public async Task<IReadOnlyList<ProviderStatus>> GetStatusesAsync(Guid userId, CancellationToken ct)
    {
        var connections = await db.ProviderConnections.AsNoTracking()
            .Where(c => c.UserId == userId)
            .ToDictionaryAsync(c => c.Provider, ct);

        return _options.Providers
            .Select(p =>
            {
                var key = p.Key.ToLowerInvariant();
                connections.TryGetValue(key, out var connection);
                return new ProviderStatus(key, p.Value.DisplayName, p.Value.Category, p.Value.Enabled,
                    connection is not null, connection?.ConnectedAtUtc);
            })
            .ToList();
    }

    public async Task<ServiceResult<ConnectResult>> StartAuthorizationAsync(Guid userId, string provider, CancellationToken ct)
    {
        provider = provider.ToLowerInvariant();
        var settings = Settings(provider);
        if (settings is null)
        {
            return ServiceResult<ConnectResult>.Fail(404, "This integration is not available.");
        }

        try
        {
            var metadata = await oauth.DiscoverAsync(settings, ct);
            var redirectUri = oauth.RedirectUriFor(provider);
            var (clientId, _) = await oauth.GetClientAsync(provider, settings, metadata, redirectUri, ct);
            var (verifier, challenge) = McpOAuthClient.CreatePkce();
            var state = SecureHash.RandomToken(32);

            // Clean up this user's stale attempts and any expired ones.
            var now = DateTime.UtcNow;
            await db.PendingAuthorizations
                .Where(p => p.ExpiresAtUtc < now || (p.UserId == userId && p.Provider == provider))
                .ExecuteDeleteAsync(ct);

            db.PendingAuthorizations.Add(new PendingAuthorization
            {
                StateHash = SecureHash.Sha256(state),
                UserId = userId,
                Provider = provider,
                CodeVerifierEncrypted = protector.Protect(verifier),
                RedirectUri = redirectUri,
                ExpiresAtUtc = now.AddMinutes(10)
            });
            await db.SaveChangesAsync(ct);

            var url = QueryHelpers.AddQueryString(metadata.AuthorizationEndpoint, new Dictionary<string, string?>
            {
                ["response_type"] = "code",
                ["client_id"] = clientId,
                ["redirect_uri"] = redirectUri,
                ["code_challenge"] = challenge,
                ["code_challenge_method"] = "S256",
                ["state"] = state,
                ["scope"] = settings.Scope
            });

            return ServiceResult<ConnectResult>.Ok(new ConnectResult(url), $"Continue to {settings.DisplayName} to approve access.");
        }
        catch (Exception ex) when (ex is OAuthException or HttpRequestException)
        {
            logger.LogWarning(ex, "Could not start authorization for {Provider}", provider);
            return ServiceResult<ConnectResult>.Fail(502, ex is OAuthException ? ex.Message : $"{settings.DisplayName} is unreachable right now.");
        }
    }

    /// <summary>Handles the browser redirect back from the provider. Returns the frontend URL to redirect to.</summary>
    public async Task<string> CompleteAuthorizationAsync(string provider, string? code, string? state, string? error, CancellationToken ct)
    {
        provider = provider.ToLowerInvariant();
        string Redirect(string status, string? message = null) =>
            QueryHelpers.AddQueryString(_options.FrontendIntegrationsUrl, new Dictionary<string, string?>
            {
                ["provider"] = provider,
                ["status"] = status,
                ["message"] = message
            });

        var settings = Settings(provider);
        if (settings is null || string.IsNullOrEmpty(state))
        {
            return Redirect("error", "Invalid authorization response.");
        }

        var pending = await db.PendingAuthorizations.FindAsync([SecureHash.Sha256(state)], ct);
        if (pending is null || pending.Provider != provider || pending.ExpiresAtUtc < DateTime.UtcNow)
        {
            return Redirect("error", "The authorization request expired. Please try again.");
        }

        db.PendingAuthorizations.Remove(pending);
        await db.SaveChangesAsync(ct);

        if (!string.IsNullOrEmpty(error) || string.IsNullOrEmpty(code))
        {
            return Redirect("error", $"{settings.DisplayName} authorization was cancelled.");
        }

        try
        {
            var metadata = await oauth.DiscoverAsync(settings, ct);
            var (clientId, clientSecret) = await oauth.GetClientAsync(provider, settings, metadata, pending.RedirectUri, ct);
            var tokens = await oauth.ExchangeCodeAsync(settings, metadata, clientId, clientSecret, code,
                protector.Unprotect(pending.CodeVerifierEncrypted), pending.RedirectUri, ct);

            await SaveTokensAsync(pending.UserId, provider, tokens, ct);
            logger.LogInformation("User {UserId} connected {Provider}", pending.UserId, provider);
            return Redirect("connected");
        }
        catch (Exception ex) when (ex is OAuthException or HttpRequestException)
        {
            logger.LogWarning(ex, "Authorization callback failed for {Provider}", provider);
            return Redirect("error", ex is OAuthException ? ex.Message : $"{settings.DisplayName} is unreachable right now.");
        }
    }

    public async Task DisconnectAsync(Guid userId, string provider, CancellationToken ct)
    {
        provider = provider.ToLowerInvariant();
        await db.ProviderConnections.Where(c => c.UserId == userId && c.Provider == provider).ExecuteDeleteAsync(ct);
        await db.SavedAddresses.Where(a => a.UserId == userId && a.Provider == provider).ExecuteDeleteAsync(ct);
    }

    /// <summary>
    /// Runs <paramref name="work"/> inside an authorised MCP session, refreshing
    /// the access token proactively and once more if the provider rejects it.
    /// </summary>
    public async Task<T> WithSessionAsync<T>(Guid userId, string provider, Func<McpSession, Task<T>> work, CancellationToken ct)
    {
        provider = provider.ToLowerInvariant();
        var settings = Settings(provider) ?? throw new ProviderNotConnectedException(provider, "This integration is not available.");

        var connection = await db.ProviderConnections.FirstOrDefaultAsync(c => c.UserId == userId && c.Provider == provider, ct)
                         ?? throw new ProviderNotConnectedException(provider, $"Connect {settings.DisplayName} first.");

        if (connection.AccessTokenExpiresAtUtc - RefreshSkew <= DateTime.UtcNow)
        {
            await RefreshAsync(connection, settings, ct);
        }

        var http = httpClientFactory.CreateClient("mcp");
        try
        {
            var session = await McpSession.OpenAsync(http, settings.McpUrl, protector.Unprotect(connection.AccessTokenEncrypted), ct);
            return await work(session);
        }
        catch (McpUnauthorizedException)
        {
            await RefreshAsync(connection, settings, ct);
            var session = await McpSession.OpenAsync(http, settings.McpUrl, protector.Unprotect(connection.AccessTokenEncrypted), ct);
            try
            {
                return await work(session);
            }
            catch (McpUnauthorizedException)
            {
                throw new ProviderNotConnectedException(provider, $"Your {settings.DisplayName} session has ended. Please reconnect.");
            }
        }
    }

    private async Task RefreshAsync(ProviderConnection connection, ProviderSettings settings, CancellationToken ct)
    {
        if (connection.RefreshTokenEncrypted is null)
        {
            throw new ProviderNotConnectedException(connection.Provider, $"Your {settings.DisplayName} session has ended. Please reconnect.");
        }

        try
        {
            var metadata = await oauth.DiscoverAsync(settings, ct);
            var (clientId, clientSecret) = await oauth.GetClientAsync(connection.Provider, settings, metadata, oauth.RedirectUriFor(connection.Provider), ct);
            var tokens = await oauth.RefreshAsync(settings, metadata, clientId, clientSecret, protector.Unprotect(connection.RefreshTokenEncrypted), ct);
            ApplyTokens(connection, tokens);
            await db.SaveChangesAsync(ct);
        }
        catch (OAuthException)
        {
            throw new ProviderNotConnectedException(connection.Provider, $"Your {settings.DisplayName} session has ended. Please reconnect.");
        }
    }

    private async Task SaveTokensAsync(Guid userId, string provider, TokenSet tokens, CancellationToken ct)
    {
        var connection = await db.ProviderConnections.FirstOrDefaultAsync(c => c.UserId == userId && c.Provider == provider, ct);
        if (connection is null)
        {
            connection = new ProviderConnection { UserId = userId, Provider = provider };
            db.ProviderConnections.Add(connection);
        }

        connection.ConnectedAtUtc = DateTime.UtcNow;
        ApplyTokens(connection, tokens);
        await db.SaveChangesAsync(ct);
    }

    private void ApplyTokens(ProviderConnection connection, TokenSet tokens)
    {
        connection.AccessTokenEncrypted = protector.Protect(tokens.AccessToken);
        if (!string.IsNullOrEmpty(tokens.RefreshToken))
        {
            // Providers rotate refresh tokens; always keep only the newest.
            connection.RefreshTokenEncrypted = protector.Protect(tokens.RefreshToken);
        }

        connection.AccessTokenExpiresAtUtc = DateTime.UtcNow.AddSeconds(tokens.ExpiresInSeconds);
        connection.Scope = tokens.Scope;
        connection.UpdatedAtUtc = DateTime.UtcNow;
    }
}
