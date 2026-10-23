using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Caching.Memory;
using Orvanta.FoodService.Ondc.Crypto;
using Orvanta.FoodService.Ondc.Protocol;

namespace Orvanta.FoodService.Ondc.Network;

/// <summary>
/// Looks up other participants' signing public keys in the ONDC registry, to verify the signatures on
/// their callbacks. Keys are cached for an hour.
/// </summary>
public sealed class OndcRegistry(
    OndcIdentity identity,
    IHttpClientFactory httpClientFactory,
    IMemoryCache cache,
    ILogger<OndcRegistry> logger)
{
    private static readonly TimeSpan CacheFor = TimeSpan.FromHours(1);

    /// <summary>Base64 Ed25519 public key for subscriber + key id, or null when the registry doesn't know it.</summary>
    public async Task<string?> SigningPublicKeyAsync(string subscriberId, string uniqueKeyId, CancellationToken ct)
    {
        var cacheKey = $"ondc-key:{subscriberId}|{uniqueKeyId}";
        if (cache.TryGetValue(cacheKey, out string? cached))
        {
            return cached;
        }

        var body = new JsonObject
        {
            ["subscriber_id"] = subscriberId,
            ["ukId"] = uniqueKeyId,
            ["domain"] = identity.Options.Domain,
            ["country"] = identity.Options.Country
        }.ToJsonString();
        var bytes = Encoding.UTF8.GetBytes(body);

        using var request = new HttpRequestMessage(HttpMethod.Post, identity.RegistryUrl + identity.Options.RegistryLookupPath)
        {
            Content = new ByteArrayContent(bytes) { Headers = { { "Content-Type", "application/json" } } }
        };
        // Newer registry versions only answer signed lookups; older ones ignore the header.
        request.Headers.TryAddWithoutValidation("Authorization", OndcAuthHeader.Create(identity.SubscriberId, identity.UniqueKeyId,
            identity.SigningKey, bytes, TimeSpan.FromSeconds(identity.Options.SignatureValiditySeconds)));

        try
        {
            using var response = await httpClientFactory.CreateClient("ondc").SendAsync(request, ct);
            var json = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("ONDC registry lookup for {Subscriber} failed: HTTP {Status}", subscriberId, (int)response.StatusCode);
                return null;
            }

            var entries = JsonNode.Parse(json) as JsonArray ?? [];
            var match = entries.OfType<JsonObject>().FirstOrDefault(e =>
                OndcReader.Text(e["subscriber_id"]) == subscriberId
                && (OndcReader.Text(e["ukId"]) ?? OndcReader.Text(e["unique_key_id"])) == uniqueKeyId);
            var key = OndcReader.Text(match?["signing_public_key"]);
            if (key is not null)
            {
                cache.Set(cacheKey, key, CacheFor);
            }
            return key;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            logger.LogWarning(ex, "ONDC registry lookup for {Subscriber} failed", subscriberId);
            return null;
        }
    }
}
