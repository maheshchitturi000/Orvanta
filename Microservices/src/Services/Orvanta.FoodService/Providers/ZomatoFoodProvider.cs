using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using Orvanta.FoodService.Integrations.Mcp;
using Orvanta.FoodService.Options;

namespace Orvanta.FoodService.Providers;

/// <summary>
/// Zomato MCP server (https://mcp-server.zomato.com/mcp).
/// Zomato has not published tool schemas yet; tool names come from its public
/// listing and argument names follow its snake_case convention. Responses are
/// read tolerantly. Verify against the live <c>tools/list</c> once your client
/// is allow-listed and adjust the argument names here if needed.
/// </summary>
public sealed class ZomatoFoodProvider(IOptions<FoodProvidersOptions> options, ILogger<ZomatoFoodProvider> logger) : IFoodProvider
{
    private const int RestaurantsToScan = 3;

    public string Key => "zomato";

    private ProviderSettings Settings => options.Value.Providers[Key];

    public async Task<IReadOnlyList<DeliveryAddress>> GetAddressesAsync(McpSession session, CancellationToken ct)
    {
        var result = await session.CallToolAsync("get_saved_addresses_for_user", new JsonObject(), ct);

        return JsonRead.ObjectsWith(result, "address_id", "display_address", "full_address", "address")
            .Select(a => new DeliveryAddress(
                JsonRead.Str(a, "address_id", "id") ?? string.Empty,
                JsonRead.Str(a, "alias", "tag", "label", "address_type"),
                JsonRead.Str(a, "display_address", "full_address", "address", "display_subtitle"),
                JsonRead.Str(a, "phone", "phone_number", "mobile")))
            .Where(a => a.Id.Length > 0)
            .ToList();
    }

    public async Task<IReadOnlyList<FoodItem>> SearchDishesAsync(McpSession session, string query, string addressId, bool vegOnly, CancellationToken ct)
    {
        var restaurants = await session.CallToolAsync("get_restaurants_for_keyword",
            new JsonObject { ["keyword"] = query, ["address_id"] = addressId }, ct);

        var candidates = JsonRead.ObjectsWith(restaurants, "res_id", "restaurant_id")
            .Select(r => new
            {
                Id = JsonRead.Str(r, "res_id", "restaurant_id", "id") ?? string.Empty,
                Name = JsonRead.Str(r, "name", "res_name", "restaurant_name") ?? "Restaurant",
                Rating = JsonRead.Dbl(r, "rating", "res_rating", "aggregate_rating"),
                Votes = JsonRead.Int(r, "votes", "rating_count", "total_ratings")
            })
            .Where(r => r.Id.Length > 0)
            .Take(RestaurantsToScan)
            .ToList();

        var words = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var items = new List<FoodItem>();

        foreach (var restaurant in candidates)
        {
            try
            {
                var menu = await session.CallToolAsync("get_menu_items_listing",
                    new JsonObject { ["res_id"] = restaurant.Id, ["address_id"] = addressId, ["keyword"] = query }, ct);

                items.AddRange(JsonRead.ObjectsWith(menu, "item_id", "catalogue_id", "variant_id")
                    .Select(i => new FoodItem(
                        Key,
                        Settings.DisplayName,
                        JsonRead.Str(i, "item_id", "catalogue_id", "variant_id", "id") ?? string.Empty,
                        JsonRead.Str(i, "name", "item_name") ?? "Dish",
                        restaurant.Id,
                        restaurant.Name,
                        JsonRead.Dec(i, "price", "final_price", "display_price"),
                        JsonRead.Dbl(i, "rating", "item_rating") ?? restaurant.Rating,
                        JsonRead.Int(i, "votes", "rating_count") ?? restaurant.Votes,
                        JsonRead.Bool(i, "is_veg", "veg"),
                        JsonRead.Bool(i, "is_bestseller", "bestseller") ?? false,
                        JsonRead.Bool(i, "in_stock", "is_available") ?? true,
                        JsonRead.HasNonEmptyArray(i, "modifier_groups") || JsonRead.HasNonEmptyArray(i, "variants"),
                        JsonRead.Str(i, "image_url", "image", "img")))
                    .Where(i => i.MenuItemId.Length > 0
                                && words.Any(w => i.Name.Contains(w, StringComparison.OrdinalIgnoreCase))
                                && (!vegOnly || i.IsVeg == true)));
            }
            catch (McpException ex)
            {
                logger.LogInformation(ex, "Zomato menu lookup failed for restaurant {RestaurantId}", restaurant.Id);
            }
        }

        return items;
    }

    public async Task<CartPreview> PrepareCartAsync(McpSession session, CartRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(Settings.CashPaymentMethod))
        {
            throw new FoodProviderException(
                "Zomato's API hasn't confirmed cash on delivery yet, so Orvanta can't place this order as COD. Pick a Swiggy option instead.");
        }

        var cart = await session.CallToolAsync("create_cart", new JsonObject
        {
            ["res_id"] = request.RestaurantId,
            ["address_id"] = request.AddressId,
            ["payment_type"] = Settings.CashPaymentMethod,
            ["items"] = new JsonArray(new JsonObject { ["item_id"] = request.MenuItemId, ["quantity"] = request.Quantity })
        }, ct);

        var breakdown = JsonRead.Find(cart, "charge_breakdown") ?? cart;
        var toPay = JsonRead.Dec(breakdown, "total", "to_pay", "grand_total", "final_amount") ?? 0;

        return new CartPreview(
            Key,
            Settings.DisplayName,
            request.RestaurantName,
            [new CartLine(JsonRead.Str(cart, "item_name") ?? "Item", request.Quantity, JsonRead.Dec(breakdown, "item_total", "subtotal") ?? toPay)],
            JsonRead.Dec(breakdown, "item_total", "subtotal") ?? toPay,
            JsonRead.Dec(breakdown, "delivery_charge", "delivery_fee") ?? 0,
            JsonRead.Dec(breakdown, "taxes", "taxes_and_charges") ?? 0,
            JsonRead.Dec(breakdown, "discount") ?? 0,
            toPay,
            Settings.CashPaymentMethod,
            request.AddressId,
            JsonRead.Find(cart, "cart_id") is { } id && id.ValueKind is JsonValueKind.String or JsonValueKind.Number
                ? (id.ValueKind == JsonValueKind.String ? id.GetString() : id.GetRawText())
                : null);
    }

    public async Task<PlacedOrder> PlaceCashOrderAsync(McpSession session, CartPreview cart, string? noteToRestaurant, CancellationToken ct)
    {
        if (cart.CartId is null)
        {
            throw new FoodProviderException("Zomato did not return a cart to check out. Please try again.");
        }

        var result = await session.CallToolAsync("checkout_cart", new JsonObject { ["cart_id"] = cart.CartId }, ct);

        var orderId = JsonRead.Str(result, "order_id", "orderId") ?? (JsonRead.Find(result, "order_id") is { } o ? o.ToString() : null);
        var message = JsonRead.Str(result, "message") ?? "Your Zomato order has been placed.";
        return new PlacedOrder(orderId, JsonRead.Str(result, "status") ?? "success", cart.ToPay, message);
    }
}
