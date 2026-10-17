namespace Orvanta.FoodService.Options;

public sealed class FoodProvidersOptions
{
    public const string SectionName = "FoodProviders";

    /// <summary>Public base URL of the integrations API (through the Gateway). Callback = {base}/{provider}/callback.</summary>
    public string CallbackBaseUrl { get; set; } = "http://localhost:5095/api/integrations";

    /// <summary>Angular page the user returns to after the provider's consent screen.</summary>
    public string FrontendIntegrationsUrl { get; set; } = "http://localhost:4200/integrations";

    public string ClientName { get; set; } = "Orvanta AI";

    public int TopResults { get; set; } = 5;

    public Dictionary<string, ProviderSettings> Providers { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class ProviderSettings
{
    public bool Enabled { get; set; } = true;
    public string DisplayName { get; set; } = string.Empty;
    public string Category { get; set; } = "food";

    /// <summary>MCP streamable-HTTP endpoint, e.g. https://mcp.swiggy.com/food.</summary>
    public string McpUrl { get; set; } = string.Empty;

    /// <summary>OAuth authorization server base (RFC 8414 metadata is discovered from here).</summary>
    public string AuthServerUrl { get; set; } = string.Empty;

    public string Scope { get; set; } = "mcp:tools";

    /// <summary>Optional pre-registered client id. When empty, Dynamic Client Registration (RFC 7591) is used.</summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>
    /// Optional path on the auth server that revokes the provider session (Swiggy: /auth/logout).
    /// Called on disconnect so the next connect asks the user to sign in again.
    /// </summary>
    public string LogoutPath { get; set; } = string.Empty;

    /// <summary>"json" (Swiggy) or "form" (RFC 6749 default) for the token endpoint body.</summary>
    public string TokenRequestFormat { get; set; } = "form";

    /// <summary>
    /// Provider-specific value for cash on delivery. Empty means the provider
    /// has not confirmed COD over its API, and ordering is refused with a clear message.
    /// </summary>
    public string CashPaymentMethod { get; set; } = string.Empty;
}
