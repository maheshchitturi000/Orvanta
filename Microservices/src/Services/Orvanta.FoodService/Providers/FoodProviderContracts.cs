using Orvanta.FoodService.Integrations.Mcp;

namespace Orvanta.FoodService.Providers;

public sealed record DeliveryAddress(string Id, string? Label, string? AddressLine, string? PhoneNumber);

/// <summary>One selected option inside a variant group (e.g. group "Size" → "Half").</summary>
public sealed record VariantChoice(string GroupId, string VariationId);

public sealed record FoodItem(
    string Provider,
    string ProviderName,
    string MenuItemId,
    string Name,
    string RestaurantId,
    string RestaurantName,
    decimal? Price,
    double? Rating,
    int? RatingCount,
    bool? IsVeg,
    bool IsBestseller,
    bool InStock,
    /// <summary>True only when the restaurant forces a choice we can't make for the user (e.g. required add-ons).</summary>
    bool RequiresCustomization,
    string? ImageUrl,
    /// <summary>Provider-specific variant format ("variants" or "variantsV2"); null when the dish has no variants.</summary>
    string? VariantFormat = null,
    /// <summary>The restaurant's default option per variant group, used when ordering.</summary>
    IReadOnlyList<VariantChoice>? DefaultVariants = null,
    /// <summary>Human readable default option, e.g. "Half · Serves 1".</summary>
    string? VariantLabel = null);

public sealed record CartLine(string Name, int Quantity, decimal Total);

public sealed record CartPreview(
    string Provider,
    string ProviderName,
    string RestaurantName,
    IReadOnlyList<CartLine> Items,
    decimal ItemTotal,
    decimal DeliveryFee,
    decimal Taxes,
    decimal Discount,
    decimal ToPay,
    string PaymentMethod,
    string AddressId,
    /// <summary>Provider cart reference when the provider needs one to check out.</summary>
    string? CartId);

public sealed record CartRequest(
    string RestaurantId,
    string RestaurantName,
    string MenuItemId,
    int Quantity,
    string AddressId,
    string? VariantFormat = null,
    IReadOnlyList<VariantChoice>? Variants = null);

public sealed record PlacedOrder(string? ProviderOrderId, string Status, decimal? Total, string Message);

/// <summary>Raised for expected, user-explainable provider failures (e.g. COD not available).</summary>
public sealed class FoodProviderException(string message, int statusCode = 422) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}

/// <summary>
/// Contract every food platform implements. The search, ranking and ordering
/// services depend only on this interface, so adding a platform means adding
/// one class and one config entry. Future categories (e.g. travel) get their
/// own contract alongside this one.
/// </summary>
public interface IFoodProvider
{
    /// <summary>Config key, e.g. "swiggy".</summary>
    string Key { get; }

    Task<IReadOnlyList<DeliveryAddress>> GetAddressesAsync(McpSession session, CancellationToken ct);

    Task<IReadOnlyList<FoodItem>> SearchDishesAsync(McpSession session, string query, string addressId, bool vegOnly, CancellationToken ct);

    /// <summary>Replaces the provider cart with the item and returns the bill, verifying cash on delivery is available.</summary>
    Task<CartPreview> PrepareCartAsync(McpSession session, CartRequest request, CancellationToken ct);

    /// <summary>Places the order currently in the cart with cash on delivery.</summary>
    Task<PlacedOrder> PlaceCashOrderAsync(McpSession session, CartPreview cart, string? noteToRestaurant, CancellationToken ct);
}
