using System.Text.Json;
using Orvanta.FoodService.Ondc.Data;
using Orvanta.FoodService.Ondc.Network;
using Orvanta.FoodService.Ondc.Protocol;
using Orvanta.Shared.Responses;

namespace Orvanta.FoodService.Ondc;

public sealed record OndcSearchResult(Guid TransactionId, string Query, string City, IReadOnlyList<OndcCatalogItem> Items);

/// <summary>An order with its quote lines and delivery details unpacked for the API.</summary>
public sealed record OndcOrderView(OndcOrder Order, IReadOnlyList<OndcQuoteLine> QuoteLines, OndcDelivery? Delivery);

/// <summary>
/// Orvanta as an ONDC buyer app: search the network, get a seller's quote, place a cash-on-delivery order and
/// follow its status. Each step sends a signed request and waits briefly for the seller's callback.
/// </summary>
public sealed class OndcBuyerService(
    OndcIdentity identity,
    OndcMessages messages,
    OndcClient client,
    IOndcRepository repository,
    OndcCallbackSignals signals,
    ILogger<OndcBuyerService> logger)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private TimeSpan ActionWait => TimeSpan.FromSeconds(identity.Options.ActionWaitSeconds);

    public async Task<ServiceResult<OndcSearchResult>> SearchAsync(Guid userId, string query, string? city, CancellationToken ct)
    {
        query = query.Trim();
        city = string.IsNullOrWhiteSpace(city) ? identity.Options.City : city.Trim();

        var transactionId = Guid.NewGuid();
        await repository.CreateSearchAsync(transactionId, userId, query, city, ct);

        var ack = await client.SearchAsync(messages.Search(transactionId, query, city), ct);
        if (!ack.Accepted)
        {
            await repository.MarkSearchFailedAsync(transactionId, ack.Error ?? "NACK", ct);
            return ServiceResult<OndcSearchResult>.Fail(502, $"The ONDC network didn't accept the search: {ack.Error}");
        }

        // Sellers answer independently; collect what arrives within the window. Late answers are still stored and
        // can be read with GetSearchAsync.
        await Task.Delay(TimeSpan.FromSeconds(identity.Options.SearchWaitSeconds), ct);
        var items = await repository.ListCatalogAsync(transactionId, userId, ct);

        return ServiceResult<OndcSearchResult>.Ok(new OndcSearchResult(transactionId, query, city, items),
            items.Count == 0 ? "No restaurants answered yet. Try again in a moment." : $"{items.Count} dishes found on ONDC.");
    }

    public async Task<ServiceResult<OndcSearchResult>> GetSearchAsync(Guid userId, Guid transactionId, CancellationToken ct)
    {
        var items = await repository.ListCatalogAsync(transactionId, userId, ct);
        var city = items.FirstOrDefault()?.City ?? identity.Options.City;
        return ServiceResult<OndcSearchResult>.Ok(new OndcSearchResult(transactionId, string.Empty, city, items));
    }

    /// <summary>Asks the seller for the final price (item + delivery + packing + taxes) via select / on_select.</summary>
    public async Task<ServiceResult<OndcOrderView>> QuoteAsync(Guid userId, long catalogItemId, int quantity, CancellationToken ct)
    {
        var item = await repository.GetCatalogItemAsync(catalogItemId, userId, ct);
        if (item is null)
        {
            return ServiceResult<OndcOrderView>.Fail(404, "That dish is no longer available. Search again.");
        }

        // ONDC keeps one transaction from search to confirm; a second order from the same search starts a new one.
        var transactionId = await repository.GetOrderByTransactionAsync(item.TransactionId, ct) is null ? item.TransactionId : Guid.NewGuid();
        var order = new OndcOrder
        {
            OrderId = Guid.NewGuid(),
            UserId = userId,
            TransactionId = transactionId,
            BppId = item.BppId,
            BppUri = item.BppUri,
            ProviderId = item.ProviderId,
            ProviderName = item.ProviderName,
            LocationId = item.LocationId,
            ItemId = item.ItemId,
            ItemName = item.ItemName,
            City = item.City,
            Quantity = quantity,
            Currency = item.Currency,
            State = OndcOrderStates.Quoting
        };
        await repository.CreateOrderAsync(order, ct);

        var ack = await client.SendToSellerAsync(order.BppUri, "select", messages.Select(order), ct);
        return await AfterSendAsync(order, ack, "on_select", ct);
    }

    /// <summary>Sends delivery details (init / on_init), then places the order with cash on delivery (confirm / on_confirm).</summary>
    public async Task<ServiceResult<OndcOrderView>> PlaceOrderAsync(Guid userId, Guid orderId, OndcDelivery delivery, CancellationToken ct)
    {
        var order = await repository.GetOrderAsync(orderId, userId, ct);
        if (order is null)
        {
            return ServiceResult<OndcOrderView>.Fail(404, "Order not found.");
        }
        if (order.State != OndcOrderStates.Quoted)
        {
            return ServiceResult<OndcOrderView>.Fail(409, $"This order can't be placed while it is {order.State}.");
        }

        await repository.UpdateOrderAsync(orderId, new OndcOrderUpdate(OndcOrderStates.Initializing,
            DeliveryJson: JsonSerializer.Serialize(delivery, Json)), ct);
        var initAck = await client.SendToSellerAsync(order.BppUri, "init", messages.Init(order, delivery), ct);
        var initialized = await AfterSendAsync(order, initAck, "on_init", ct);
        if (initialized.Value?.Order.State != OndcOrderStates.Initialized)
        {
            return initialized;
        }

        var current = initialized.Value.Order;
        await repository.UpdateOrderAsync(orderId, new OndcOrderUpdate(OndcOrderStates.Confirming), ct);
        var confirmAck = await client.SendToSellerAsync(current.BppUri, "confirm", messages.Confirm(current, delivery), ct);
        return await AfterSendAsync(current, confirmAck, "on_confirm", ct);
    }

    /// <summary>Asks the seller for the latest order state (status / on_status).</summary>
    public async Task<ServiceResult<OndcOrderView>> RefreshStatusAsync(Guid userId, Guid orderId, CancellationToken ct)
    {
        var order = await repository.GetOrderAsync(orderId, userId, ct);
        if (order is null)
        {
            return ServiceResult<OndcOrderView>.Fail(404, "Order not found.");
        }
        if (order.BppOrderId is null)
        {
            return ServiceResult<OndcOrderView>.Ok(View(order), "This order hasn't been placed yet.");
        }

        var ack = await client.SendToSellerAsync(order.BppUri, "status", messages.Status(order), ct);
        var latest = ack.Accepted
            ? await WaitForOrderAsync(order, "on_status", o => o.UpdatedAtUtc > order.UpdatedAtUtc, ct)
            : order;
        return ServiceResult<OndcOrderView>.Ok(View(latest), ack.Accepted ? $"Order is {latest.State}." : $"Couldn't refresh: {ack.Error}");
    }

    public async Task<ServiceResult<OndcOrderView>> GetOrderAsync(Guid userId, Guid orderId, CancellationToken ct) =>
        await repository.GetOrderAsync(orderId, userId, ct) is { } order
            ? ServiceResult<OndcOrderView>.Ok(View(order))
            : ServiceResult<OndcOrderView>.Fail(404, "Order not found.");

    public async Task<ServiceResult<IReadOnlyList<OndcOrderView>>> ListOrdersAsync(Guid userId, CancellationToken ct) =>
        ServiceResult<IReadOnlyList<OndcOrderView>>.Ok((await repository.ListOrdersAsync(userId, 20, ct)).Select(View).ToList());

    /// <summary>After a request: NACK fails the order; ACK waits for the seller's callback, then returns the stored state.</summary>
    private async Task<ServiceResult<OndcOrderView>> AfterSendAsync(OndcOrder order, OndcAck ack, string callback, CancellationToken ct)
    {
        if (!ack.Accepted)
        {
            await repository.UpdateOrderAsync(order.OrderId, new OndcOrderUpdate(OndcOrderStates.Failed, Error: ack.Error), ct);
            var failed = await repository.GetOrderAsync(order.OrderId, order.UserId, ct) ?? order;
            return ServiceResult<OndcOrderView>.Fail(502, $"The restaurant didn't accept the request: {ack.Error}", View(failed));
        }

        var latest = await WaitForOrderAsync(order, callback, o => !OndcOrderStates.IsPending(o.State), ct);

        if (OndcOrderStates.IsPending(latest.State))
        {
            logger.LogWarning("ONDC {Callback} for order {OrderId} did not arrive within {Seconds}s", callback, order.OrderId, ActionWait.TotalSeconds);
            return ServiceResult<OndcOrderView>.Ok(View(latest), "The restaurant hasn't answered yet. Check again in a moment.");
        }
        return latest.State == OndcOrderStates.Failed
            ? ServiceResult<OndcOrderView>.Fail(422, latest.Error ?? "The restaurant declined the request.", View(latest))
            : ServiceResult<OndcOrderView>.Ok(View(latest));
    }

    /// <summary>
    /// Waits for the seller's callback: wakes as soon as it is signalled, and re-reads the order every second in
    /// case the answer arrived before waiting began (fast sellers) or on another instance.
    /// </summary>
    private async Task<OndcOrder> WaitForOrderAsync(OndcOrder order, string callback, Func<OndcOrder, bool> done, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + ActionWait;
        while (true)
        {
            var latest = await repository.GetOrderAsync(order.OrderId, order.UserId, ct) ?? order;
            var remaining = deadline - DateTime.UtcNow;
            if (done(latest) || remaining <= TimeSpan.Zero)
            {
                return latest;
            }
            await signals.WaitAsync(order.TransactionId.ToString(), callback, remaining < TimeSpan.FromSeconds(1) ? remaining : TimeSpan.FromSeconds(1), ct);
        }
    }

    private static OndcOrderView View(OndcOrder order)
    {
        var lines = string.IsNullOrEmpty(order.QuoteBreakupJson)
            ? []
            : OndcReader.QuoteLines(System.Text.Json.Nodes.JsonNode.Parse(order.QuoteBreakupJson) as System.Text.Json.Nodes.JsonArray ?? []);
        var delivery = string.IsNullOrEmpty(order.DeliveryJson) ? null : JsonSerializer.Deserialize<OndcDelivery>(order.DeliveryJson, Json);
        return new OndcOrderView(order, lines, delivery);
    }
}
