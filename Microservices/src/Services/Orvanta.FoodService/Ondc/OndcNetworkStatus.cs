namespace Orvanta.FoodService.Ondc;

public sealed record OndcChecklistItem(string Label, bool Done);

/// <summary>How Orvanta is connected to ONDC, and what is still missing to go live.</summary>
public sealed record OndcNetworkStatus(
    bool Enabled,
    string Mode,
    string SubscriberId,
    string SubscriberUrl,
    string Domain,
    string City,
    bool ReadyForNetwork,
    IReadOnlyList<OndcChecklistItem> Checklist);

public static class OndcNetworkStatusFactory
{
    public static OndcNetworkStatus Create(OndcIdentity identity)
    {
        var o = identity.Options;
        var https = identity.SubscriberUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
        var checklist = new List<OndcChecklistItem>
        {
            new("Subscriber ID (your registered domain)", !string.IsNullOrWhiteSpace(o.SubscriberId)),
            new("Public HTTPS callback URL", https),
            new("Signing key pair (Ed25519)", !string.IsNullOrWhiteSpace(o.SigningPrivateKey)),
            new("Encryption key pair (X25519) for on_subscribe", !string.IsNullOrWhiteSpace(o.EncryptionPrivateKey)),
            new("ONDC encryption public key for the environment", !string.IsNullOrWhiteSpace(o.OndcEncryptionPublicKey)),
            new("Site verification request id", !string.IsNullOrWhiteSpace(o.SiteVerificationRequestId)),
            new("Gateway and registry URLs", !string.IsNullOrWhiteSpace(o.GatewayUrl) && !string.IsNullOrWhiteSpace(o.RegistryUrl))
        };

        return new OndcNetworkStatus(
            o.Enabled,
            o.Mode.ToString(),
            identity.SubscriberId,
            identity.SubscriberUrl,
            o.Domain,
            o.City,
            ReadyForNetwork: checklist.All(c => c.Done),
            checklist);
    }
}
