using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using Orvanta.FoodService.Integrations.Mcp;
using Orvanta.FoodService.Options;

namespace Orvanta.FoodService.Providers;

/// <summary>
/// Swiggy Food MCP server (https://mcp.swiggy.com/food).
/// Tool reference: https://mcp.swiggy.com/builders/docs/reference/food/
/// </summary>
public sealed class SwiggyFoodProvider(IOptions<FoodProvidersOptions> options) : IFoodProvider
{
    public string Key => "swiggy";

    private ProviderSettings Settings => options.Value.Providers[Key];

    public async Task<IReadOnlyList<DeliveryAddress>> GetAddressesAsync(McpSession session, CancellationToken ct)
    {
        var result = await session.CallToolAsync("get_addresses", new JsonObject { ["page"] = 1, ["pageSize"] = 10 }, ct);

        return JsonRead.ObjectsWith(result, "addressLine")
            .Select(a => new DeliveryAddress(
                JsonRead.Str(a, "id") ?? string.Empty,
                JsonRead.Str(a, "addressTag", "addressCategory"),
                JsonRead.Str(a, "addressLine"),
                JsonRead.Str(a, "phoneNumber")))
            .Where(a => a.Id.Length > 0)
            .ToList();
    }

    public async Task<IReadOnlyList<FoodItem>> SearchDishesAsync(McpSession session, string query, string addressId, bool vegOnly, CancellationToken ct)
    {
        var args = new JsonObject { ["query"] = query, ["addressId"] = addressId };
        if (vegOnly)
        {
            args["vegFilter"] = 1;
        }

        var result = await session.CallToolAsync("search_menu", args, ct);

        return JsonRead.ObjectsWith(result, "menu_item_id")
            .Select(i => new FoodItem(
                Key,
                Settings.DisplayName,
                JsonRead.Str(i, "menu_item_id")!,
                JsonRead.Str(i, "name") ?? "Dish",
                JsonRead.Str(i, "restaurant_id") ?? string.Empty,
                JsonRead.Str(i, "restaurant_name") ?? "Restaurant",
                PriceOf(i),
                JsonRead.Dbl(i, "rating"),
                JsonRead.Int(i, "totalRatings"),
                JsonRead.Bool(i, "isVeg"),
                JsonRead.Bool(i, "isBestseller") ?? false,
                (JsonRead.Int(i, "inStock") ?? 1) > 0,
                JsonRead.Bool(i, "hasVariants") == true || JsonRead.Bool(i, "hasAddons") == true
                    || JsonRead.HasNonEmptyArray(i, "variations") || JsonRead.HasNonEmptyArray(i, "variantsV2"),
                JsonRead.Str(i, "imageUrl")))
            .Where(i => i.RestaurantId.Length > 0)
            .ToList();
    }

    public async Task<CartPreview> PrepareCartAsync(McpSession session, CartRequest request, CancellationToken ct)
    {
        await session.CallToolAsync("flush_food_cart", new JsonObject(), ct);

        await session.CallToolAsync("update_food_cart", new JsonObject
        {
            ["restaurantId"] = request.RestaurantId,
            ["restaurantName"] = request.RestaurantName,
            ["addressId"] = request.AddressId,
            ["cartItems"] = new JsonArray(new JsonObject
            {
                ["menu_item_id"] = request.MenuItemId,
                ["quantity"] = request.Quantity
            })
        }, ct);

        // update_food_cart doesn't render the bill; the docs say to read the cart right after.
        var cart = await session.CallToolAsync("get_food_cart", new JsonObject { ["addressId"] = request.AddressId }, ct);
        var paymentOptions = await session.CallToolAsync("get_payment_options", new JsonObject { ["addressId"] = request.AddressId }, ct);

        var cashMethod = string.IsNullOrWhiteSpace(Settings.CashPaymentMethod) ? "Cash" : Settings.CashPaymentMethod;
        if (!JsonRead.ContainsText(paymentOptions, "cash") && !JsonRead.ContainsText(paymentOptions, "cod"))
        {
            throw new FoodProviderException("Cash on delivery isn't available for this order on Swiggy right now.");
        }

        var pricing = JsonRead.Find(cart, "pricing") ?? cart;
        var lines = JsonRead.ObjectsWith(cart, "menu_item_id")
            .Select(i => new CartLine(
                JsonRead.Str(i, "name") ?? "Item",
                JsonRead.Int(i, "quantity") ?? 1,
                JsonRead.Dec(i, "final_price", "total", "subtotal") ?? 0))
            .ToList();

        var discount = JsonRead.Find(cart, "offers") is { } offers ? JsonRead.Dec(offers, "coupon_discount") ?? 0 : 0;

        return new CartPreview(
            Key,
            Settings.DisplayName,
            JsonRead.Find(cart, "restaurant") is { ValueKind: JsonValueKind.Object } r ? JsonRead.Str(r, "name") ?? request.RestaurantName : request.RestaurantName,
            lines,
            JsonRead.Dec(pricing, "item_total") ?? lines.Sum(l => l.Total),
            JsonRead.Dec(pricing, "delivery_charge") ?? 0,
            JsonRead.Dec(pricing, "taxes_and_charges") ?? 0,
            discount,
            JsonRead.Dec(pricing, "to_pay") ?? lines.Sum(l => l.Total),
            cashMethod,
            request.AddressId,
            JsonRead.Find(cart, "cart_id") is { ValueKind: JsonValueKind.String } id ? id.GetString() : null);
    }

    public async Task<PlacedOrder> PlaceCashOrderAsync(McpSession session, CartPreview cart, string? noteToRestaurant, CancellationToken ct)
    {
        var args = new JsonObject { ["addressId"] = cart.AddressId, ["paymentMethod"] = cart.PaymentMethod };
        if (!string.IsNullOrWhiteSpace(noteToRestaurant))
        {
            args["noteToRestaurant"] = noteToRestaurant;
        }

        var result = await session.CallToolAsync("place_food_order", args, ct);

        var status = JsonRead.Find(result, "normalizedStatus", "status") is { ValueKind: JsonValueKind.String } s ? s.GetString()! : "success";
        if (status.Equals("PENDING_PAYMENT", StringComparison.OrdinalIgnoreCase))
        {
            throw new FoodProviderException("Swiggy asked for online payment instead of cash. The order was not placed.");
        }

        var orderId = JsonRead.Find(result, "orderId", "order_id") is { } o && o.ValueKind is JsonValueKind.String or JsonValueKind.Number
            ? (o.ValueKind == JsonValueKind.String ? o.GetString() : o.GetRawText())
            : null;

        var message = JsonRead.Find(result, "message") is { ValueKind: JsonValueKind.String } m ? m.GetString()! : "Your Swiggy order has been placed.";
        return new PlacedOrder(orderId, status, cart.ToPay, message);
    }

    private static decimal? PriceOf(JsonElement item)
    {
        var price = JsonRead.Dec(item, "price");
        if (price is not null) return price;

        // Variant-only items carry the price on the default variation.
        if (item.TryGetProperty("variations", out var variations) && variations.ValueKind == JsonValueKind.Array)
        {
            var preferred = variations.EnumerateArray().FirstOrDefault(v => JsonRead.Bool(v, "default") == true);
            if (preferred.ValueKind == JsonValueKind.Object) return JsonRead.Dec(preferred, "price");
            if (variations.GetArrayLength() > 0) return JsonRead.Dec(variations[0], "price");
        }

        return null;
    }
}
