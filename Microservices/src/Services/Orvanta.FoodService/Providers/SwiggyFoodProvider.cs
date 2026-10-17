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
            .Select(ToFoodItem)
            .Where(i => i.RestaurantId.Length > 0)
            .ToList();
    }

    private FoodItem ToFoodItem(JsonElement i)
    {
        var defaults = DefaultVariants(i);

        return new FoodItem(
            Key,
            Settings.DisplayName,
            JsonRead.Str(i, "menu_item_id")!,
            JsonRead.Str(i, "name") ?? "Dish",
            JsonRead.Str(i, "restaurant_id") ?? string.Empty,
            JsonRead.Str(i, "restaurant_name") ?? "Restaurant",
            defaults?.Price ?? JsonRead.Dec(i, "price"),
            JsonRead.Dbl(i, "rating"),
            JsonRead.Int(i, "totalRatings"),
            JsonRead.Bool(i, "isVeg"),
            JsonRead.Bool(i, "isBestseller") ?? false,
            (JsonRead.Int(i, "inStock") ?? 1) > 0,
            // Default variants are picked automatically; only mandatory add-ons need the user.
            RequiresCustomization: HasRequiredAddons(i) || defaults is { Resolved: false },
            JsonRead.Str(i, "imageUrl"),
            defaults?.Format,
            defaults?.Choices,
            defaults?.Label);
    }

    private sealed record DefaultSelection(string Format, IReadOnlyList<VariantChoice> Choices, string Label, decimal? Price, bool Resolved);

    /// <summary>
    /// Picks the restaurant's default option in each variant group (falling back to the first in-stock one).
    /// Items use either "variations" (legacy, sent back as "variants") or "variantsV2", never both.
    /// </summary>
    private static DefaultSelection? DefaultVariants(JsonElement item)
    {
        if (item.TryGetProperty("variantsV2", out var groups) && groups.ValueKind == JsonValueKind.Array && groups.GetArrayLength() > 0)
        {
            var choices = new List<VariantChoice>();
            var names = new List<string>();
            decimal? price = null;

            foreach (var group in groups.EnumerateArray())
            {
                var groupId = JsonRead.Str(group, "groupId", "group_id");
                var pick = PickDefault(group.TryGetProperty("variations", out var v) ? v : default);
                if (groupId is null || pick is null) return new DefaultSelection("variantsV2", [], string.Empty, null, false);

                choices.Add(new VariantChoice(groupId, JsonRead.Str(pick.Value, "id")!));
                names.Add(JsonRead.Str(pick.Value, "name") ?? string.Empty);
                price = JsonRead.Dec(pick.Value, "price") ?? price;
            }

            return new DefaultSelection("variantsV2", choices, string.Join(" · ", names.Where(n => n.Length > 0)), price, true);
        }

        if (item.TryGetProperty("variations", out var variations) && variations.ValueKind == JsonValueKind.Array && variations.GetArrayLength() > 0)
        {
            var choices = new List<VariantChoice>();
            var names = new List<string>();
            decimal? price = null;

            foreach (var group in variations.EnumerateArray().GroupBy(x => JsonRead.Str(x, "groupId", "group_id") ?? string.Empty))
            {
                var pick = PickDefault(group);
                if (group.Key.Length == 0 || pick is null) return new DefaultSelection("variants", [], string.Empty, null, false);

                choices.Add(new VariantChoice(group.Key, JsonRead.Str(pick.Value, "id")!));
                names.Add(JsonRead.Str(pick.Value, "name") ?? string.Empty);
                price = JsonRead.Dec(pick.Value, "price") ?? price;
            }

            return new DefaultSelection("variants", choices, string.Join(" · ", names.Where(n => n.Length > 0)), price, true);
        }

        return null;
    }

    private static JsonElement? PickDefault(IEnumerable<JsonElement> options)
    {
        var list = options.Where(o => o.ValueKind == JsonValueKind.Object && JsonRead.Str(o, "id") is not null).ToList();
        var inStock = list.Where(o => (JsonRead.Int(o, "inStock") ?? 1) > 0).ToList();
        var pick = inStock.FirstOrDefault(o => JsonRead.Bool(o, "default") == true);
        if (pick.ValueKind == JsonValueKind.Object) return pick;
        return inStock.Count > 0 ? inStock[0] : null;
    }

    private static JsonElement? PickDefault(JsonElement array) =>
        array.ValueKind == JsonValueKind.Array ? PickDefault(array.EnumerateArray()) : null;

    private static bool HasMandatoryAddons(JsonElement cart) =>
        JsonRead.Find(cart, "valid_addons") is { ValueKind: JsonValueKind.Array } groups
        && groups.EnumerateArray().Any(g => (JsonRead.Int(g, "minAddons", "min_addons") ?? 0) > 0);

    private static bool HasRequiredAddons(JsonElement item) =>
        item.TryGetProperty("addons", out var addons)
        && addons.ValueKind == JsonValueKind.Array
        && addons.EnumerateArray().Any(g => (JsonRead.Int(g, "minAddons") ?? 0) > 0);

    public async Task<CartPreview> PrepareCartAsync(McpSession session, CartRequest request, CancellationToken ct)
    {
        await session.CallToolAsync("flush_food_cart", new JsonObject(), ct);

        var cartItem = new JsonObject
        {
            ["menu_item_id"] = request.MenuItemId,
            ["quantity"] = request.Quantity
        };

        // Use the same format the item came with: "variants" (legacy "variations") or "variantsV2".
        if (request.Variants is { Count: > 0 } && request.VariantFormat is "variants" or "variantsV2")
        {
            cartItem[request.VariantFormat] = new JsonArray(request.Variants
                .Select(v => (JsonNode)new JsonObject { ["group_id"] = v.GroupId, ["variation_id"] = v.VariationId })
                .ToArray());
        }

        await session.CallToolAsync("update_food_cart", new JsonObject
        {
            ["restaurantId"] = request.RestaurantId,
            ["restaurantName"] = request.RestaurantName,
            ["addressId"] = request.AddressId,
            ["cartItems"] = new JsonArray(cartItem)
        }, ct);

        // update_food_cart doesn't render the bill; the docs say to read the cart right after.
        var cart = await session.CallToolAsync("get_food_cart",
            new JsonObject { ["addressId"] = request.AddressId, ["restaurantName"] = request.RestaurantName }, ct);

        if (!JsonRead.ObjectsWith(cart, "menu_item_id").Any())
        {
            throw new FoodProviderException("Swiggy didn't accept this item into the cart. It may need choices that can only be made in the Swiggy app.");
        }

        // Search results only say "hasAddons"; the cart tells us whether any add-on group is mandatory.
        if (HasMandatoryAddons(cart))
        {
            await session.CallToolAsync("flush_food_cart", new JsonObject(), ct);
            throw new FoodProviderException("This dish needs an add-on choice that the restaurant requires. Please pick another dish or order it in the Swiggy app.");
        }
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

}
