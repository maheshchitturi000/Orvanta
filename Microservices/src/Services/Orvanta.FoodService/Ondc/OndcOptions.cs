namespace Orvanta.FoodService.Ondc;

public enum OndcMode
{
    /// <summary>Built-in local network (gateway, sellers, registry) for development. No registration needed.</summary>
    Sandbox,
    Staging,
    PreProduction,
    Production
}

/// <summary>
/// "Ondc" section. Orvanta joins ONDC as a buyer app (BAP). Keys belong in user-secrets or a vault, never in
/// appsettings. In <see cref="OndcMode.Sandbox"/> the subscriber, URLs and keys default to the local sandbox.
/// </summary>
public sealed class OndcOptions
{
    public const string SectionName = "Ondc";

    public bool Enabled { get; set; } = true;
    public OndcMode Mode { get; set; } = OndcMode.Sandbox;

    /// <summary>ONDC domain. RET11 = food and beverage.</summary>
    public string Domain { get; set; } = "ONDC:RET11";
    public string CoreVersion { get; set; } = "1.2.0";
    public string Country { get; set; } = "IND";

    /// <summary>Default city as an ONDC std code, e.g. "std:080" (Bengaluru), "std:044" (Chennai).</summary>
    public string City { get; set; } = "std:080";

    /// <summary>Orvanta's registered subscriber id (its public domain), e.g. "buyer.orvanta.ai".</summary>
    public string SubscriberId { get; set; } = string.Empty;

    /// <summary>Public base URL ONDC calls back on: {SubscriberUrl}/on_search, /on_select, … (HTTPS outside the sandbox).</summary>
    public string SubscriberUrl { get; set; } = string.Empty;

    /// <summary>Key id registered with ONDC for the signing key below.</summary>
    public string UniqueKeyId { get; set; } = string.Empty;

    /// <summary>Base64 Ed25519 private key (32-byte seed, or the 64-byte libsodium form ONDC's tools print).</summary>
    public string SigningPrivateKey { get; set; } = string.Empty;

    /// <summary>Base64 X25519 private key (raw or DER) used only to answer ONDC's on_subscribe challenge.</summary>
    public string EncryptionPrivateKey { get; set; } = string.Empty;

    /// <summary>ONDC's X25519 public key for the environment (from ONDC's onboarding docs), used with on_subscribe.</summary>
    public string OndcEncryptionPublicKey { get; set; } = string.Empty;

    /// <summary>The request id entered when subscribing; signed into /ondc-site-verification.html.</summary>
    public string SiteVerificationRequestId { get; set; } = string.Empty;

    public string GatewayUrl { get; set; } = string.Empty;
    public string RegistryUrl { get; set; } = string.Empty;

    /// <summary>Registry lookup path (relative to <see cref="RegistryUrl"/>).</summary>
    public string RegistryLookupPath { get; set; } = "/lookup";

    /// <summary>How long a search collects sellers' on_search answers before returning.</summary>
    public int SearchWaitSeconds { get; set; } = 6;

    /// <summary>How long select / init / confirm / status wait for the seller's answer.</summary>
    public int ActionWaitSeconds { get; set; } = 20;

    /// <summary>Signed requests are valid this long.</summary>
    public int SignatureValiditySeconds { get; set; } = 300;

    /// <summary>Reject callbacks whose signature doesn't verify against the registry. Never turn off outside the sandbox.</summary>
    public bool VerifyCallbackSignatures { get; set; } = true;

    public bool IsSandbox => Mode == OndcMode.Sandbox;
}
