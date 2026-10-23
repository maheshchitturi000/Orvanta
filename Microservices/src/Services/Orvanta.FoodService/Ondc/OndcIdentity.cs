using Microsoft.Extensions.Options;
using Orvanta.FoodService.Ondc.Crypto;

namespace Orvanta.FoodService.Ondc;

/// <summary>
/// Who Orvanta is on the network and where it talks to, resolved once at start-up. In the sandbox everything
/// defaults to the local simulator and a throwaway key pair is generated if none is configured; in staging and
/// production every value must be configured (the service refuses to start otherwise).
/// </summary>
public sealed class OndcIdentity
{
    public const string SandboxSubscriberId = "buyer.orvanta.local";
    public const string SandboxUniqueKeyId = "orvanta-sandbox-key";

    public OndcIdentity(IOptions<OndcOptions> options, IConfiguration configuration)
    {
        var o = options.Value;
        Options = o;

        var foodServiceUrl = (configuration["Ondc:LocalBaseUrl"] ?? "http://localhost:5077").TrimEnd('/');
        LocalBaseUrl = foodServiceUrl;

        SubscriberId = Pick(o.SubscriberId, o.IsSandbox ? SandboxSubscriberId : null, nameof(o.SubscriberId));
        SubscriberUrl = Pick(o.SubscriberUrl, o.IsSandbox ? $"{foodServiceUrl}/ondc" : null, nameof(o.SubscriberUrl)).TrimEnd('/');
        UniqueKeyId = Pick(o.UniqueKeyId, o.IsSandbox ? SandboxUniqueKeyId : null, nameof(o.UniqueKeyId));
        GatewayUrl = Pick(o.GatewayUrl, o.IsSandbox ? $"{foodServiceUrl}/ondc-sandbox/gateway" : null, nameof(o.GatewayUrl)).TrimEnd('/');
        RegistryUrl = Pick(o.RegistryUrl, o.IsSandbox ? $"{foodServiceUrl}/ondc-sandbox/registry" : null, nameof(o.RegistryUrl)).TrimEnd('/');

        SigningKey = string.IsNullOrWhiteSpace(o.SigningPrivateKey)
            ? o.IsSandbox ? OndcCrypto.GenerateSigningKey() : throw Missing(nameof(o.SigningPrivateKey))
            : OndcCrypto.LoadSigningKey(o.SigningPrivateKey);
    }

    public OndcOptions Options { get; }

    /// <summary>This service's own base URL; the sandbox gateway, seller and registry live under it.</summary>
    public string LocalBaseUrl { get; }
    public string SubscriberId { get; }
    public string SubscriberUrl { get; }
    public string UniqueKeyId { get; }
    public string GatewayUrl { get; }
    public string RegistryUrl { get; }
    public OndcSigningKey SigningKey { get; }

    private string Pick(string configured, string? sandboxDefault, string name)
    {
        if (!string.IsNullOrWhiteSpace(configured)) return configured.Trim();
        return sandboxDefault ?? throw Missing(name);
    }

    private InvalidOperationException Missing(string name) =>
        new($"Ondc:{name} must be configured when Ondc:Mode is {Options.Mode}. See the ONDC section of Microservices/README.md.");
}
