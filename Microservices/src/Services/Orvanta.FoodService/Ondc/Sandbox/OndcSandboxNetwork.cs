using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Orvanta.FoodService.Ondc.Crypto;
using Orvanta.FoodService.Ondc.Protocol;

namespace Orvanta.FoodService.Ondc.Sandbox;

/// <summary>
/// A tiny local ONDC network for development: a gateway, one seller app (BPP) with sample restaurants, and a
/// registry. It speaks the real protocol: requests are verified, every reply is an ACK followed by a signed
/// callback to the buyer's bap_uri, exactly as a real seller would. Only active when Ondc:Mode is Sandbox.
/// </summary>
public sealed class OndcSandboxNetwork(
    OndcIdentity identity,
    IHttpClientFactory httpClientFactory,
    IHostApplicationLifetime lifetime,
    ILogger<OndcSandboxNetwork> logger)
{
    public const string SellerId = "seller.sandbox.orvanta.local";
    public const string SellerKeyId = "sandbox-seller-key";

    private const decimal DeliveryCharge = 30m;
    private const decimal PackingCharge = 15m;
    private const decimal TaxRate = 0.05m;

    /// <summary>The seller's own key, new on every start (the registry below is the only place it is published).</summary>
    private readonly OndcSigningKey _sellerKey = OndcCrypto.GenerateSigningKey();

    /// <summary>Confirmed orders: id → when placed, to advance status over time.</summary>
    private readonly ConcurrentDictionary<string, DateTime> _orders = new();

    public string SellerUri => $"{identity.LocalBaseUrl}/ondc-sandbox/seller";

    /// <summary>Registry entries: Orvanta (the buyer) and the sandbox seller.</summary>
    public JsonArray Lookup(string? subscriberId, string? uniqueKeyId)
    {
        var entries = new[]
        {
            (Id: identity.SubscriberId, KeyId: identity.UniqueKeyId, Key: identity.SigningKey.PublicKeyBase64, Url: identity.SubscriberUrl, Type: "BAP"),
            (Id: SellerId, KeyId: SellerKeyId, Key: _sellerKey.PublicKeyBase64, Url: SellerUri, Type: "BPP")
        };

        return new JsonArray(entries
            .Where(e => (subscriberId is null || e.Id == subscriberId) && (uniqueKeyId is null || e.KeyId == uniqueKeyId))
            .Select(e => (JsonNode)new JsonObject
            {
                ["subscriber_id"] = e.Id,
                ["ukId"] = e.KeyId,
                ["subscriber_url"] = e.Url,
                ["type"] = e.Type,
                ["domain"] = identity.Options.Domain,
                ["country"] = identity.Options.Country,
                ["signing_public_key"] = e.Key,
                ["status"] = "SUBSCRIBED"
            })
            .ToArray());
    }

    /// <summary>Only Orvanta talks to the sandbox, so a request must be signed with Orvanta's key.</summary>
    public bool IsSignedByBuyer(byte[] body, string? authorization) =>
        OndcAuthHeader.Parse(authorization) is { } header
        && header.SubscriberId == identity.SubscriberId
        && header.IsValid(identity.SigningKey.PublicKeyBase64, body);

    /// <summary>Handles a gateway or seller action after it was ACKed: builds the seller's answer and sends it back.</summary>
    public void Reply(string action, JsonObject request)
    {
        var token = lifetime.ApplicationStopping;
        _ = Task.Run(async () =>
        {
            try
            {
                // A real seller answers after its ACK; the short pause keeps that order.
                await Task.Delay(TimeSpan.FromMilliseconds(400), token);
                var reply = action switch
                {
                    "search" => OnSearch(request),
                    "select" => OnSelect(request),
                    "init" => OnInit(request),
                    "confirm" => OnConfirm(request),
                    "status" => OnStatus(request),
                    _ => null
                };
                if (reply is not null)
                {
                    await SendAsync(request, "on_" + action, reply, token);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "ONDC sandbox could not answer {Action}", action);
            }
        }, token);
    }

    private JsonObject? OnSearch(JsonObject request)
    {
        var query = OndcReader.Text(request["message"]?["intent"]?["item"]?["descriptor"]?["name"]) ?? string.Empty;
        var matches = OndcSandboxCatalog.Search(query).ToList();
        if (matches.Count == 0)
        {
            return null; // sellers with nothing to offer stay silent
        }

        var providers = new JsonArray(matches.Select(m => (JsonNode)new JsonObject
        {
            ["id"] = m.Restaurant.Id,
            ["descriptor"] = new JsonObject { ["name"] = m.Restaurant.Name },
            ["locations"] = new JsonArray(new JsonObject { ["id"] = m.Restaurant.LocationId, ["gps"] = m.Restaurant.Gps }),
            ["items"] = new JsonArray(m.Dishes.Select(d => (JsonNode)new JsonObject
            {
                ["id"] = d.Id,
                ["descriptor"] = new JsonObject { ["name"] = d.Name },
                ["price"] = Price(d.Price),
                ["location_id"] = m.Restaurant.LocationId,
                ["category_id"] = d.Category,
                ["tags"] = new JsonArray(new JsonObject
                {
                    ["code"] = "veg_nonveg",
                    ["list"] = new JsonArray(new JsonObject { ["code"] = d.IsVeg ? "veg" : "non_veg", ["value"] = "yes" })
                })
            }).ToArray())
        }).ToArray());

        return new JsonObject
        {
            ["catalog"] = new JsonObject
            {
                ["bpp/descriptor"] = new JsonObject { ["name"] = "Orvanta Sandbox Seller" },
                ["bpp/providers"] = providers
            }
        };
    }

    private JsonObject OnSelect(JsonObject request) => QuotedOrder(request);

    private JsonObject OnInit(JsonObject request)
    {
        var message = QuotedOrder(request);
        if (message["order"] is JsonObject order && request["message"]?["order"] is JsonObject asked)
        {
            order["billing"] = asked["billing"]?.DeepClone();
            order["fulfillments"] = asked["fulfillments"]?.DeepClone();
            order["payment"] = asked["payment"]?.DeepClone();
        }
        return message;
    }

    private JsonObject OnConfirm(JsonObject request)
    {
        var order = request["message"]?["order"]?.DeepClone() as JsonObject ?? new JsonObject();
        var id = OndcReader.Text(order["id"]) ?? Guid.NewGuid().ToString();
        _orders[id] = DateTime.UtcNow;
        order["id"] = id;
        order["state"] = "Created";
        return new JsonObject { ["order"] = order };
    }

    private JsonObject OnStatus(JsonObject request)
    {
        var id = OndcReader.Text(request["message"]?["order_id"]) ?? string.Empty;
        if (!_orders.TryGetValue(id, out var placedAt))
        {
            return Error("40002", "Order not found at the sandbox seller (it restarts empty).");
        }

        // Moves along with time so the status refresh has something to show.
        var age = DateTime.UtcNow - placedAt;
        var state = age < TimeSpan.FromMinutes(1) ? "Accepted" : age < TimeSpan.FromMinutes(3) ? "In-progress" : "Completed";
        return new JsonObject { ["order"] = new JsonObject { ["id"] = id, ["state"] = state } };
    }

    /// <summary>Item × quantity + delivery + packing + 5% tax on the item.</summary>
    private static JsonObject QuotedOrder(JsonObject request)
    {
        var asked = request["message"]?["order"];
        var providerId = OndcReader.Text(asked?["provider"]?["id"]);
        var item = (asked?["items"] as JsonArray)?.FirstOrDefault();
        var itemId = OndcReader.Text(item?["id"]);
        var quantity = (int)(OndcReader.Amount(item?["quantity"]?["count"]) ?? 1);

        if (OndcSandboxCatalog.Find(providerId, itemId) is not { } found)
        {
            return Error("40002", "Item not available.");
        }
        if (quantity is < 1 or > 20)
        {
            return Error("40009", "Quantity must be 1-20.");
        }

        var itemTotal = found.Dish.Price * quantity;
        var tax = Math.Round(itemTotal * TaxRate, 2);
        var total = itemTotal + DeliveryCharge + PackingCharge + tax;

        return new JsonObject
        {
            ["order"] = new JsonObject
            {
                ["provider"] = new JsonObject { ["id"] = found.Restaurant.Id },
                ["items"] = new JsonArray(new JsonObject { ["id"] = found.Dish.Id, ["quantity"] = new JsonObject { ["count"] = quantity } }),
                ["quote"] = new JsonObject
                {
                    ["price"] = Price(total),
                    ["breakup"] = new JsonArray(
                        Line(found.Dish.Id, $"{found.Dish.Name} × {quantity}", "item", itemTotal),
                        Line(found.Dish.Id, "Delivery charges", "delivery", DeliveryCharge),
                        Line(found.Dish.Id, "Packing charges", "packing", PackingCharge),
                        Line(found.Dish.Id, "Taxes", "tax", tax)),
                    ["ttl"] = "PT15M"
                }
            }
        };
    }

    private async Task SendAsync(JsonObject request, string action, JsonObject message, CancellationToken ct)
    {
        var context = request["context"]!.DeepClone().AsObject();
        var bapUri = OndcReader.Text(context["bap_uri"]) ?? identity.SubscriberUrl;
        context["action"] = action;
        context["bpp_id"] = SellerId;
        context["bpp_uri"] = SellerUri;
        context["timestamp"] = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

        var body = new JsonObject { ["context"] = context };
        if (message["error"] is JsonNode error)
        {
            body["error"] = error.DeepClone();
        }
        else
        {
            body["message"] = message;
        }

        var bytes = Encoding.UTF8.GetBytes(body.ToJsonString());
        using var post = new HttpRequestMessage(HttpMethod.Post, $"{bapUri.TrimEnd('/')}/{action}")
        {
            Content = new ByteArrayContent(bytes) { Headers = { { "Content-Type", "application/json" } } }
        };
        post.Headers.TryAddWithoutValidation("Authorization",
            OndcAuthHeader.Create(SellerId, SellerKeyId, _sellerKey, bytes, TimeSpan.FromSeconds(identity.Options.SignatureValiditySeconds)));

        using var response = await httpClientFactory.CreateClient("ondc").SendAsync(post, ct);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("ONDC sandbox {Action} was rejected by the buyer: HTTP {Status}", action, (int)response.StatusCode);
        }
    }

    private static JsonObject Price(decimal value) => new()
    {
        ["currency"] = "INR",
        ["value"] = value.ToString("0.00", CultureInfo.InvariantCulture)
    };

    private static JsonObject Line(string itemId, string title, string type, decimal amount) => new()
    {
        ["@ondc/org/item_id"] = itemId,
        ["@ondc/org/title_type"] = type,
        ["title"] = title,
        ["price"] = Price(amount)
    };

    private static JsonObject Error(string code, string text) => new()
    {
        ["error"] = new JsonObject { ["type"] = "DOMAIN-ERROR", ["code"] = code, ["message"] = text }
    };
}
