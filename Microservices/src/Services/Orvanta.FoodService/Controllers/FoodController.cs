using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orvanta.FoodService.Services;
using Orvanta.Shared.Responses;
using Orvanta.Shared.Security;

namespace Orvanta.FoodService.Controllers;

[Route("api/food")]
[Authorize(Policy = "UserOnly")]
public sealed class FoodController(FoodSearchService search, FoodOrderService orders) : ApiControllerBase
{
    /// <summary>Detects food intent in a chat prompt and returns the top ranked dishes across connected providers.</summary>
    [HttpPost("assist")]
    public async Task<ActionResult<ResponseCls<FoodAssistResult>>> Assist(FoodAssistRequest request, CancellationToken ct)
    {
        var result = await search.AssistAsync(User.GetUserId(), request.Prompt, request.VegOnly, ct);

        var message = !result.IsFoodIntent
            ? "No food request detected."
            : result.Items.Count > 0
                ? $"Here are the top {result.Items.Count} picks for \"{result.Query}\"."
                : result.ProvidersSearched.Count == 0
                    ? "Connect Swiggy or Zomato to search for food."
                    : $"I couldn't find \"{result.Query}\" near you right now.";

        return Respond(ServiceResult<FoodAssistResult>.Ok(result, message));
    }

    [HttpPost("orders/preview")]
    public async Task<ActionResult<ResponseCls<OrderPreview>>> Preview(OrderPreviewRequest request, CancellationToken ct) =>
        Respond(await orders.PreviewAsync(User.GetUserId(), request, ct));

    [HttpPost("orders/confirm")]
    public async Task<ActionResult<ResponseCls<OrderConfirmation>>> Confirm(OrderConfirmRequest request, CancellationToken ct) =>
        Respond(await orders.ConfirmAsync(User.GetUserId(), request, ct));

    [HttpGet("orders")]
    public async Task<ActionResult<ResponseCls<IReadOnlyList<OrderHistoryItem>>>> History(CancellationToken ct) =>
        Respond(ServiceResult<IReadOnlyList<OrderHistoryItem>>.Ok(await orders.HistoryAsync(User.GetUserId(), ct)));
}

public sealed class FoodAssistRequest
{
    [Required(ErrorMessage = "Tell me what you'd like to eat.")]
    [StringLength(500)]
    public string Prompt { get; set; } = string.Empty;

    public bool? VegOnly { get; set; }
}
