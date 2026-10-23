using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Orvanta.FoodService.Ondc.Data;

namespace Orvanta.FoodService.Ondc.Protocol;

/// <summary>One line of a seller's quote (item, delivery, packing, tax, discount…).</summary>
public sealed record OndcQuoteLine(string Title, string Type, decimal Amount);

public sealed record OndcQuote(decimal Total, string Currency, IReadOnlyList<OndcQuoteLine> Lines, string BreakupJson);

/// <summary>The common envelope of every ONDC message.</summary>
public sealed record OndcEnvelope(
    string Action,
    string TransactionId,
    string MessageId,
    string? BapId,
    string? BppId,
    string? BppUri,
    JsonNode? Message,
    string? Error);

/// <summary>Reads sellers' replies defensively: missing or oddly typed fields never throw.</summary>
public static class OndcReader
{
    public static OndcEnvelope? Envelope(JsonNode? root)
    {
        var context = root?["context"];
        var action = Text(context?["action"]);
        var transactionId = Text(context?["transaction_id"]);
        if (action is null || transactionId is null)
        {
            return null;
        }

        var error = root?["error"] is { } e && (Text(e["message"]) ?? Text(e["code"])) is { } msg
            ? $"{Text(e["code"]) ?? "error"}: {msg}"
            : null;

        return new OndcEnvelope(action, transactionId, Text(context?["message_id"]) ?? string.Empty,
            Text(context?["bap_id"]), Text(context?["bpp_id"]), Text(context?["bpp_uri"]), root?["message"], error);
    }

    /// <summary>on_search: every item of every provider in the seller's catalog.</summary>
    public static IReadOnlyList<OndcCatalogItem> CatalogItems(OndcEnvelope envelope)
    {
        var items = new List<OndcCatalogItem>();
        if (envelope.BppId is null || envelope.BppUri is null || envelope.Message?["catalog"] is not JsonObject catalog)
        {
            return items;
        }

        var providers = catalog["bpp/providers"] as JsonArray ?? catalog["providers"] as JsonArray ?? [];
        foreach (var provider in providers.OfType<JsonObject>())
        {
            var providerId = Text(provider["id"]);
            if (providerId is null) continue;
            var providerName = Text(provider["descriptor"]?["name"]) ?? providerId;
            var defaultLocation = Text((provider["locations"] as JsonArray)?.FirstOrDefault()?["id"]);

            foreach (var item in (provider["items"] as JsonArray ?? []).OfType<JsonObject>())
            {
                var itemId = Text(item["id"]);
                var name = Text(item["descriptor"]?["name"]);
                if (itemId is null || name is null) continue;

                items.Add(new OndcCatalogItem
                {
                    BppId = envelope.BppId,
                    BppUri = envelope.BppUri,
                    ProviderId = providerId,
                    ProviderName = providerName,
                    LocationId = Text(item["location_id"]) ?? defaultLocation,
                    ItemId = itemId,
                    ItemName = name,
                    Price = Amount(item["price"]?["value"]),
                    Currency = Text(item["price"]?["currency"]) ?? "INR",
                    IsVeg = VegFlag(item),
                    ImageUrl = Text((item["descriptor"]?["images"] as JsonArray)?.FirstOrDefault()) ??
                               Text((item["descriptor"]?["images"] as JsonArray)?.FirstOrDefault()?["url"])
                });
            }
        }
        return items;
    }

    /// <summary>on_select / on_init: the quote. Null when the seller sent none.</summary>
    public static OndcQuote? Quote(OndcEnvelope envelope)
    {
        var quote = envelope.Message?["order"]?["quote"];
        var total = Amount(quote?["price"]?["value"]);
        if (quote is null || total is null)
        {
            return null;
        }

        var breakup = quote["breakup"] as JsonArray ?? [];
        return new OndcQuote(total.Value, Text(quote["price"]?["currency"]) ?? "INR", QuoteLines(breakup), breakup.ToJsonString());
    }

    /// <summary>Quote breakup lines, also used to unpack the breakup stored with an order.</summary>
    public static IReadOnlyList<OndcQuoteLine> QuoteLines(JsonArray breakup) =>
        breakup.OfType<JsonObject>()
            .Select(b => new OndcQuoteLine(
                Text(b["title"]) ?? "Item",
                Text(b["@ondc/org/title_type"]) ?? "item",
                Amount(b["price"]?["value"]) ?? 0))
            .ToList();

    /// <summary>on_confirm / on_status: the seller's order id and state (Created, Accepted, In-progress, Completed, Cancelled).</summary>
    public static (string? OrderId, string? State) OrderState(OndcEnvelope envelope)
    {
        var order = envelope.Message?["order"];
        return (Text(order?["id"]), Text(order?["state"]));
    }

    private static bool? VegFlag(JsonObject item)
    {
        foreach (var tag in (item["tags"] as JsonArray ?? []).OfType<JsonObject>())
        {
            if (!string.Equals(Text(tag["code"]), "veg_nonveg", StringComparison.OrdinalIgnoreCase)) continue;
            foreach (var entry in (tag["list"] as JsonArray ?? []).OfType<JsonObject>())
            {
                var code = Text(entry["code"])?.ToLowerInvariant();
                var value = Text(entry["value"])?.ToLowerInvariant();
                if (value is "yes" or "true") return code == "veg";
            }
        }
        return null;
    }

    public static string? Text(JsonNode? node)
    {
        if (node is not JsonValue value) return null;
        return value.GetValueKind() switch
        {
            JsonValueKind.String => value.GetValue<string>() is { Length: > 0 } s ? s : null,
            JsonValueKind.Number => value.ToJsonString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null
        };
    }

    public static decimal? Amount(JsonNode? node) =>
        Text(node) is { } s && decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : null;
}
