using System.Globalization;
using System.Text.Json.Nodes;
using Orvanta.FoodService.Ondc.Data;

namespace Orvanta.FoodService.Ondc.Protocol;

/// <summary>Where and to whom the food is delivered (also the billing details, as ONDC requires both).</summary>
public sealed record OndcDelivery(string Name, string Phone, string AddressLine, string City, string AreaCode, string? Gps, string? Email = null);

/// <summary>Builds ONDC (Beckn) request bodies for the food domain (RET11, core 1.2.0).</summary>
public sealed class OndcMessages(OndcIdentity identity)
{
    /// <summary>City centre GPS and a central pincode per ONDC city code, used when the user gives no location.</summary>
    private static readonly Dictionary<string, (string Gps, string AreaCode)> CityDefaults = new()
    {
        ["std:080"] = ("12.9716,77.5946", "560001"), // Bengaluru
        ["std:044"] = ("13.0827,80.2707", "600001"), // Chennai
        ["std:011"] = ("28.6139,77.2090", "110001"), // Delhi
        ["std:022"] = ("19.0760,72.8777", "400001"), // Mumbai
        ["std:040"] = ("17.3850,78.4867", "500001"), // Hyderabad
        ["std:033"] = ("22.5726,88.3639", "700001"), // Kolkata
        ["std:020"] = ("18.5204,73.8567", "411001")  // Pune
    };

    public static (string Gps, string AreaCode) DefaultsFor(string city) =>
        CityDefaults.TryGetValue(city, out var d) ? d : CityDefaults["std:080"];

    public JsonObject Context(string action, Guid transactionId, string city, string? bppId = null, string? bppUri = null)
    {
        var o = identity.Options;
        var context = new JsonObject
        {
            ["domain"] = o.Domain,
            ["country"] = o.Country,
            ["city"] = city,
            ["action"] = action,
            ["core_version"] = o.CoreVersion,
            ["bap_id"] = identity.SubscriberId,
            ["bap_uri"] = identity.SubscriberUrl,
            ["transaction_id"] = transactionId.ToString(),
            ["message_id"] = Guid.NewGuid().ToString(),
            ["timestamp"] = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture),
            ["ttl"] = "PT30S"
        };
        if (bppId is not null) context["bpp_id"] = bppId;
        if (bppUri is not null) context["bpp_uri"] = bppUri;
        return context;
    }

    /// <summary>Broadcast search through the gateway: dishes matching the text, delivered to the city.</summary>
    public JsonObject Search(Guid transactionId, string query, string city)
    {
        var (gps, areaCode) = DefaultsFor(city);
        return new JsonObject
        {
            ["context"] = Context("search", transactionId, city),
            ["message"] = new JsonObject
            {
                ["intent"] = new JsonObject
                {
                    ["item"] = new JsonObject { ["descriptor"] = new JsonObject { ["name"] = query } },
                    ["fulfillment"] = new JsonObject
                    {
                        ["type"] = "Delivery",
                        ["end"] = new JsonObject
                        {
                            ["location"] = new JsonObject { ["gps"] = gps, ["address"] = new JsonObject { ["area_code"] = areaCode } }
                        }
                    },
                    ["payment"] = new JsonObject
                    {
                        ["@ondc/org/buyer_app_finder_fee_type"] = "percent",
                        ["@ondc/org/buyer_app_finder_fee_amount"] = "3"
                    }
                }
            }
        };
    }

    /// <summary>Ask one seller for a quote (price incl. delivery, packing and taxes) for an item.</summary>
    public JsonObject Select(OndcOrder order)
    {
        var city = order.City;
        var (gps, areaCode) = DefaultsFor(city);
        return new JsonObject
        {
            ["context"] = Context("select", order.TransactionId, city, order.BppId, order.BppUri),
            ["message"] = new JsonObject
            {
                ["order"] = new JsonObject
                {
                    ["provider"] = Provider(order),
                    ["items"] = Items(order),
                    ["fulfillments"] = new JsonArray(new JsonObject
                    {
                        ["end"] = new JsonObject
                        {
                            ["location"] = new JsonObject { ["gps"] = gps, ["address"] = new JsonObject { ["area_code"] = areaCode } }
                        }
                    })
                }
            }
        };
    }

    /// <summary>Send billing and delivery details; the seller answers with final terms (on_init).</summary>
    public JsonObject Init(OndcOrder order, OndcDelivery delivery) => new()
    {
        ["context"] = Context("init", order.TransactionId, order.City, order.BppId, order.BppUri),
        ["message"] = new JsonObject { ["order"] = OrderBody(order, delivery, includeQuote: false) }
    };

    /// <summary>Place the order with cash on delivery (ONDC payment type ON-FULFILLMENT, collected by the seller).</summary>
    public JsonObject Confirm(OndcOrder order, OndcDelivery delivery)
    {
        var body = OrderBody(order, delivery, includeQuote: true);
        var now = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
        body["id"] = order.OrderId.ToString();
        body["state"] = "Created";
        body["created_at"] = now;
        body["updated_at"] = now;
        return new JsonObject
        {
            ["context"] = Context("confirm", order.TransactionId, order.City, order.BppId, order.BppUri),
            ["message"] = new JsonObject { ["order"] = body }
        };
    }

    public JsonObject Status(OndcOrder order) => new()
    {
        ["context"] = Context("status", order.TransactionId, order.City, order.BppId, order.BppUri),
        ["message"] = new JsonObject { ["order_id"] = order.BppOrderId ?? order.OrderId.ToString() }
    };

    private JsonObject OrderBody(OndcOrder order, OndcDelivery d, bool includeQuote)
    {
        var gps = string.IsNullOrWhiteSpace(d.Gps) ? DefaultsFor(order.City).Gps : d.Gps;
        var address = new JsonObject
        {
            ["name"] = d.Name,
            ["building"] = d.AddressLine,
            ["locality"] = d.AddressLine,
            ["city"] = d.City,
            ["state"] = d.City,
            ["country"] = identity.Options.Country,
            ["area_code"] = d.AreaCode
        };

        var body = new JsonObject
        {
            ["provider"] = Provider(order),
            ["items"] = Items(order),
            ["billing"] = new JsonObject
            {
                ["name"] = d.Name,
                ["address"] = address.DeepClone(),
                ["phone"] = d.Phone,
                ["email"] = d.Email ?? string.Empty,
                ["created_at"] = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture),
                ["updated_at"] = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture)
            },
            ["fulfillments"] = new JsonArray(new JsonObject
            {
                ["type"] = "Delivery",
                ["end"] = new JsonObject
                {
                    ["location"] = new JsonObject { ["gps"] = gps, ["address"] = address },
                    ["contact"] = new JsonObject { ["phone"] = d.Phone, ["email"] = d.Email ?? string.Empty }
                }
            }),
            ["payment"] = new JsonObject
            {
                ["type"] = "ON-FULFILLMENT",
                ["status"] = "NOT-PAID",
                ["collected_by"] = "BPP"
            }
        };

        if (includeQuote && order.QuoteTotal is { } total)
        {
            body["quote"] = new JsonObject
            {
                ["price"] = new JsonObject { ["currency"] = order.Currency, ["value"] = total.ToString("0.00", CultureInfo.InvariantCulture) },
                ["breakup"] = string.IsNullOrEmpty(order.QuoteBreakupJson) ? new JsonArray() : JsonNode.Parse(order.QuoteBreakupJson)
            };
        }
        return body;
    }

    private static JsonObject Provider(OndcOrder order)
    {
        var provider = new JsonObject { ["id"] = order.ProviderId };
        if (!string.IsNullOrEmpty(order.LocationId))
        {
            provider["locations"] = new JsonArray(new JsonObject { ["id"] = order.LocationId });
        }
        return provider;
    }

    private static JsonArray Items(OndcOrder order)
    {
        var item = new JsonObject
        {
            ["id"] = order.ItemId,
            ["quantity"] = new JsonObject { ["count"] = order.Quantity }
        };
        if (!string.IsNullOrEmpty(order.LocationId)) item["location_id"] = order.LocationId;
        return new JsonArray(item);
    }
}
