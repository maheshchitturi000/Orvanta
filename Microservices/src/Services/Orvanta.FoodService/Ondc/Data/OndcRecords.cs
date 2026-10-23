namespace Orvanta.FoodService.Ondc.Data;

/// <summary>Row of OndcCatalogItems: one dish from one seller for one search.</summary>
public sealed record OndcCatalogItem
{
    public long CatalogItemId { get; init; }
    public Guid TransactionId { get; init; }
    public string BppId { get; init; } = string.Empty;
    public string BppUri { get; init; } = string.Empty;
    public string ProviderId { get; init; } = string.Empty;
    public string ProviderName { get; init; } = string.Empty;
    public string? LocationId { get; init; }
    public string ItemId { get; init; } = string.Empty;
    public string ItemName { get; init; } = string.Empty;
    public decimal? Price { get; init; }
    public string Currency { get; init; } = "INR";
    public bool? IsVeg { get; init; }
    public string? ImageUrl { get; init; }
    public DateTime ReceivedAtUtc { get; init; }

    /// <summary>ONDC city code of the search the item came from.</summary>
    public string City { get; init; } = string.Empty;
}

/// <summary>Row of OndcOrders.</summary>
public sealed record OndcOrder
{
    public Guid OrderId { get; init; }
    public Guid UserId { get; init; }
    public Guid TransactionId { get; init; }
    public string BppId { get; init; } = string.Empty;
    public string BppUri { get; init; } = string.Empty;
    public string ProviderId { get; init; } = string.Empty;
    public string ProviderName { get; init; } = string.Empty;
    public string? LocationId { get; init; }
    public string ItemId { get; init; } = string.Empty;
    public string ItemName { get; init; } = string.Empty;
    public string City { get; init; } = string.Empty;
    public int Quantity { get; init; }
    public string State { get; init; } = string.Empty;
    public decimal? QuoteTotal { get; init; }
    public string Currency { get; init; } = "INR";
    public string? QuoteBreakupJson { get; init; }
    public string? DeliveryJson { get; init; }
    public string? BppOrderId { get; init; }
    public string? Error { get; init; }
    public DateTime CreatedAtUtc { get; init; }
    public DateTime UpdatedAtUtc { get; init; }
}

/// <summary>An order's progress, as stored in OndcOrders.State.</summary>
public static class OndcOrderStates
{
    public const string Quoting = "Quoting";
    public const string Quoted = "Quoted";
    public const string Initializing = "Initializing";
    public const string Initialized = "Initialized";
    public const string Confirming = "Confirming";
    public const string Confirmed = "Confirmed";
    public const string Failed = "Failed";

    /// <summary>States where we are waiting for the seller to answer.</summary>
    public static bool IsPending(string state) => state is Quoting or Initializing or Confirming;
}

/// <summary>A message to write to OndcMessages.</summary>
public sealed record OndcMessageLog(
    string Direction,
    string Action,
    string? TransactionId,
    string? MessageId,
    string? Counterparty,
    string PayloadJson,
    bool? SignatureValid = null,
    int? HttpStatus = null,
    string? Error = null);

/// <summary>Values a step learned about an order; nulls keep what is stored.</summary>
public sealed record OndcOrderUpdate(
    string State,
    decimal? QuoteTotal = null,
    string? Currency = null,
    string? QuoteBreakupJson = null,
    string? DeliveryJson = null,
    string? BppOrderId = null,
    string? Error = null);
