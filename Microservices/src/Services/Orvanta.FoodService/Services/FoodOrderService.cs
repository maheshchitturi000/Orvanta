using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Orvanta.FoodService.Data;
using Orvanta.FoodService.Domain;
using Orvanta.FoodService.Integrations;
using Orvanta.FoodService.Integrations.Mcp;
using Orvanta.FoodService.Providers;
using Orvanta.Shared.Responses;

namespace Orvanta.FoodService.Services;

public sealed class OrderPreviewRequest
{
    [Required] public string Provider { get; set; } = string.Empty;
    [Required] public string RestaurantId { get; set; } = string.Empty;
    [Required] public string RestaurantName { get; set; } = string.Empty;
    [Required] public string MenuItemId { get; set; } = string.Empty;
    [Required] public string ItemName { get; set; } = string.Empty;
    [Range(1, 10, ErrorMessage = "Quantity must be between 1 and 10.")] public int Quantity { get; set; } = 1;
    public bool RequiresCustomization { get; set; }

    /// <summary>Optional: overrides the user's default address for this provider.</summary>
    public string? AddressId { get; set; }
}

public sealed class OrderConfirmRequest
{
    [Required] public Guid PreviewId { get; set; }
    [StringLength(200)] public string? NoteToRestaurant { get; set; }
}

public sealed record OrderPreview(Guid PreviewId, DateTime ExpiresAtUtc, string ItemName, CartPreview Cart);

public sealed record OrderConfirmation(Guid OrderId, string Provider, string? ProviderOrderId, string Status,
    string RestaurantName, string ItemName, int Quantity, decimal TotalAmount, string PaymentMethod, string Message);

public sealed record OrderHistoryItem(Guid OrderId, string Provider, string? ProviderOrderId, string RestaurantName,
    string ItemName, int Quantity, decimal TotalAmount, string PaymentMethod, string Status, DateTime CreatedAtUtc);

/// <summary>
/// Two-step ordering so nothing is ordered without the user seeing the bill:
/// 1) preview builds the provider cart and returns the bill (COD verified),
/// 2) confirm places that exact cart with cash on delivery.
/// </summary>
public sealed class FoodOrderService(
    FoodDbContext db,
    ProviderConnectionService connections,
    FoodProviderRegistry registry,
    AddressService addresses,
    IMemoryCache cache,
    ILogger<FoodOrderService> logger)
{
    private static readonly TimeSpan PreviewLifetime = TimeSpan.FromMinutes(10);

    private sealed record StoredPreview(Guid UserId, OrderPreviewRequest Request, CartPreview Cart);

    public async Task<ServiceResult<OrderPreview>> PreviewAsync(Guid userId, OrderPreviewRequest request, CancellationToken ct)
    {
        var provider = registry.Get(request.Provider);
        if (provider is null)
        {
            return ServiceResult<OrderPreview>.Fail(404, "This integration is not available.");
        }

        if (request.RequiresCustomization)
        {
            return ServiceResult<OrderPreview>.Fail(422,
                $"{request.ItemName} needs size or add-on choices. Please pick another item or order it in the {provider.Key} app.");
        }

        return await Guard(async () =>
        {
            var addressId = request.AddressId ?? await addresses.GetDefaultAddressIdAsync(userId, provider.Key, ct);
            if (addressId is null)
            {
                return ServiceResult<OrderPreview>.Fail(422, $"Add a delivery address in your {provider.Key} account first.");
            }

            var cart = await connections.WithSessionAsync(userId, provider.Key, s => provider.PrepareCartAsync(s,
                new CartRequest(request.RestaurantId, request.RestaurantName, request.MenuItemId, request.Quantity, addressId), ct), ct);

            var previewId = Guid.NewGuid();
            cache.Set(CacheKey(previewId), new StoredPreview(userId, request, cart), PreviewLifetime);

            return ServiceResult<OrderPreview>.Ok(
                new OrderPreview(previewId, DateTime.UtcNow.Add(PreviewLifetime), request.ItemName, cart),
                "Review your order. Payment: Cash on Delivery.");
        });
    }

    public async Task<ServiceResult<OrderConfirmation>> ConfirmAsync(Guid userId, OrderConfirmRequest request, CancellationToken ct)
    {
        if (!cache.TryGetValue<StoredPreview>(CacheKey(request.PreviewId), out var stored) || stored is null || stored.UserId != userId)
        {
            return ServiceResult<OrderConfirmation>.Fail(410, "This order preview has expired. Please review the order again.");
        }

        // Single use: a double click must never place two orders.
        cache.Remove(CacheKey(request.PreviewId));

        var provider = registry.Get(stored.Cart.Provider)!;

        return await Guard(async () =>
        {
            var placed = await connections.WithSessionAsync(userId, provider.Key,
                s => provider.PlaceCashOrderAsync(s, stored.Cart, request.NoteToRestaurant, ct), ct);

            var order = new FoodOrder
            {
                UserId = userId,
                Provider = provider.Key,
                ProviderOrderId = placed.ProviderOrderId,
                RestaurantId = stored.Request.RestaurantId,
                RestaurantName = stored.Cart.RestaurantName,
                MenuItemId = stored.Request.MenuItemId,
                ItemName = stored.Request.ItemName,
                Quantity = stored.Request.Quantity,
                TotalAmount = placed.Total ?? stored.Cart.ToPay,
                PaymentMethod = "COD",
                AddressId = stored.Cart.AddressId,
                Status = "Placed"
            };
            db.FoodOrders.Add(order);
            await db.SaveChangesAsync(ct);

            logger.LogInformation("User {UserId} placed {Provider} COD order {OrderId}", userId, provider.Key, order.Id);

            return ServiceResult<OrderConfirmation>.Created(new OrderConfirmation(
                order.Id, order.Provider, order.ProviderOrderId, placed.Status, order.RestaurantName, order.ItemName,
                order.Quantity, order.TotalAmount, "Cash on Delivery", placed.Message), placed.Message);
        });
    }

    public async Task<IReadOnlyList<OrderHistoryItem>> HistoryAsync(Guid userId, CancellationToken ct) =>
        await db.FoodOrders.AsNoTracking()
            .Where(o => o.UserId == userId)
            .OrderByDescending(o => o.CreatedAtUtc)
            .Take(50)
            .Select(o => new OrderHistoryItem(o.Id, o.Provider, o.ProviderOrderId, o.RestaurantName, o.ItemName,
                o.Quantity, o.TotalAmount, o.PaymentMethod, o.Status, o.CreatedAtUtc))
            .ToListAsync(ct);

    private async Task<ServiceResult<T>> Guard<T>(Func<Task<ServiceResult<T>>> action)
    {
        try
        {
            return await action();
        }
        catch (ProviderNotConnectedException ex)
        {
            return ServiceResult<T>.Fail(409, ex.Message);
        }
        catch (FoodProviderException ex)
        {
            return ServiceResult<T>.Fail(ex.StatusCode, ex.Message);
        }
        catch (McpException ex)
        {
            logger.LogWarning(ex, "Provider call failed");
            return ServiceResult<T>.Fail(502, $"The provider couldn't complete this: {ex.Message}");
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Provider unreachable");
            return ServiceResult<T>.Fail(502, "The provider is unreachable right now. Please try again.");
        }
    }

    private static string CacheKey(Guid previewId) => $"order-preview:{previewId}";
}
