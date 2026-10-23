using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orvanta.FoodService.Ondc;
using Orvanta.FoodService.Ondc.Protocol;
using Orvanta.Shared.Responses;
using Orvanta.Shared.Security;

namespace Orvanta.FoodService.Controllers;

public sealed class OndcSearchRequest
{
    [Required(ErrorMessage = "Enter a dish to search for.")]
    [StringLength(100, MinimumLength = 2, ErrorMessage = "Search for 2-100 characters.")]
    public string Query { get; set; } = string.Empty;

    /// <summary>ONDC city code, e.g. "std:080". Empty uses the configured default.</summary>
    [RegularExpression(@"^std:\d{2,5}$", ErrorMessage = "City must be an ONDC city code such as std:080.")]
    public string? City { get; set; }
}

public sealed class OndcQuoteRequest
{
    [Range(1, long.MaxValue, ErrorMessage = "Pick a dish.")]
    public long CatalogItemId { get; set; }

    [Range(1, 20, ErrorMessage = "Quantity must be 1-20.")]
    public int Quantity { get; set; } = 1;
}

public sealed class OndcDeliveryRequest
{
    [Required(ErrorMessage = "Enter the name for delivery."), StringLength(100)]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "Enter a mobile number."), RegularExpression(@"^[6-9]\d{9}$", ErrorMessage = "Enter a valid 10-digit mobile number.")]
    public string Phone { get; set; } = string.Empty;

    [Required(ErrorMessage = "Enter the delivery address."), StringLength(300, MinimumLength = 5)]
    public string AddressLine { get; set; } = string.Empty;

    [Required(ErrorMessage = "Enter the city."), StringLength(60)]
    public string City { get; set; } = string.Empty;

    [Required(ErrorMessage = "Enter the pincode."), RegularExpression(@"^\d{6}$", ErrorMessage = "Pincode is 6 digits.")]
    public string AreaCode { get; set; } = string.Empty;

    /// <summary>"lat,lng"; optional.</summary>
    [RegularExpression(@"^-?\d{1,2}\.\d+,-?\d{1,3}\.\d+$", ErrorMessage = "Location must be \"latitude,longitude\".")]
    public string? Gps { get; set; }

    [EmailAddress] public string? Email { get; set; }
}

/// <summary>Orvanta's buyer app on ONDC: network status, search, quote, cash-on-delivery order, order status.</summary>
[Route("api/ondc")]
[Authorize(Policy = "UserOnly")]
public sealed class OndcController(OndcBuyerService buyer, OndcIdentity identity) : ApiControllerBase
{
    [HttpGet("status")]
    public ActionResult<ResponseCls<OndcNetworkStatus>> Status() =>
        Respond(ServiceResult<OndcNetworkStatus>.Ok(OndcNetworkStatusFactory.Create(identity)));

    [HttpPost("searches")]
    public async Task<ActionResult<ResponseCls<OndcSearchResult>>> Search(OndcSearchRequest request, CancellationToken ct) =>
        Respond(await buyer.SearchAsync(User.GetUserId(), request.Query, request.City, ct));

    /// <summary>Results of an earlier search, including sellers that answered after it returned.</summary>
    [HttpGet("searches/{transactionId:guid}")]
    public async Task<ActionResult<ResponseCls<OndcSearchResult>>> GetSearch(Guid transactionId, CancellationToken ct) =>
        Respond(await buyer.GetSearchAsync(User.GetUserId(), transactionId, ct));

    /// <summary>Starts an order: asks the seller for the final price.</summary>
    [HttpPost("orders")]
    public async Task<ActionResult<ResponseCls<OndcOrderView>>> Quote(OndcQuoteRequest request, CancellationToken ct) =>
        Respond(await buyer.QuoteAsync(User.GetUserId(), request.CatalogItemId, request.Quantity, ct));

    /// <summary>Places a quoted order with cash on delivery.</summary>
    [HttpPost("orders/{orderId:guid}/place")]
    public async Task<ActionResult<ResponseCls<OndcOrderView>>> Place(Guid orderId, OndcDeliveryRequest request, CancellationToken ct) =>
        Respond(await buyer.PlaceOrderAsync(User.GetUserId(), orderId,
            new OndcDelivery(request.Name.Trim(), request.Phone, request.AddressLine.Trim(), request.City.Trim(), request.AreaCode, request.Gps, request.Email), ct));

    /// <summary>Asks the seller for the latest state of a placed order.</summary>
    [HttpPost("orders/{orderId:guid}/refresh")]
    public async Task<ActionResult<ResponseCls<OndcOrderView>>> Refresh(Guid orderId, CancellationToken ct) =>
        Respond(await buyer.RefreshStatusAsync(User.GetUserId(), orderId, ct));

    [HttpGet("orders/{orderId:guid}")]
    public async Task<ActionResult<ResponseCls<OndcOrderView>>> Get(Guid orderId, CancellationToken ct) =>
        Respond(await buyer.GetOrderAsync(User.GetUserId(), orderId, ct));

    [HttpGet("orders")]
    public async Task<ActionResult<ResponseCls<IReadOnlyList<OndcOrderView>>>> List(CancellationToken ct) =>
        Respond(await buyer.ListOrdersAsync(User.GetUserId(), ct));
}
