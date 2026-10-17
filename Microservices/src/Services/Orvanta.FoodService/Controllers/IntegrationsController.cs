using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orvanta.FoodService.Integrations;
using Orvanta.FoodService.Integrations.Mcp;
using Orvanta.FoodService.Providers;
using Orvanta.FoodService.Services;
using Orvanta.Shared.Responses;
using Orvanta.Shared.Security;

namespace Orvanta.FoodService.Controllers;

[Route("api/integrations")]
[Authorize(Policy = "UserOnly")]
public sealed class IntegrationsController(
    ProviderConnectionService connections,
    AddressService addresses,
    ILogger<IntegrationsController> logger) : ApiControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ResponseCls<IReadOnlyList<ProviderStatus>>>> List(CancellationToken ct) =>
        Respond(ServiceResult<IReadOnlyList<ProviderStatus>>.Ok(await connections.GetStatusesAsync(User.GetUserId(), ct)));

    [HttpGet("{provider}/status")]
    public async Task<ActionResult<ResponseCls<ProviderStatus>>> Status(string provider, CancellationToken ct)
    {
        var status = (await connections.GetStatusesAsync(User.GetUserId(), ct))
            .FirstOrDefault(s => s.Provider.Equals(provider, StringComparison.OrdinalIgnoreCase));

        return status is null
            ? Respond(ServiceResult<ProviderStatus>.Fail(404, "This integration is not available."))
            : Respond(ServiceResult<ProviderStatus>.Ok(status, status.IsConnected ? $"{status.DisplayName} is connected." : $"{status.DisplayName} is not connected."));
    }

    [HttpPost("{provider}/connect")]
    public async Task<ActionResult<ResponseCls<ConnectResult>>> Connect(string provider, CancellationToken ct) =>
        Respond(await connections.StartAuthorizationAsync(User.GetUserId(), provider, ct));

    /// <summary>Browser redirect target from the provider's consent screen. The user is identified by the one-time state.</summary>
    [HttpGet("{provider}/callback")]
    [AllowAnonymous]
    public async Task<IActionResult> Callback(string provider, [FromQuery] string? code, [FromQuery] string? state,
        [FromQuery] string? error, CancellationToken ct) =>
        Redirect(await connections.CompleteAuthorizationAsync(provider, code, state, error, ct));

    [HttpDelete("{provider}")]
    public async Task<ActionResult<ResponseCls<object>>> Disconnect(string provider, CancellationToken ct)
    {
        await connections.DisconnectAsync(User.GetUserId(), provider, ct);
        return Respond(ServiceResult<object>.Ok(null!, "Disconnected."));
    }

    [HttpGet("{provider}/addresses")]
    public async Task<ActionResult<ResponseCls<AddressesResult>>> Addresses(string provider, [FromQuery] bool refresh, CancellationToken ct)
    {
        try
        {
            var result = await addresses.GetAsync(User.GetUserId(), provider, refresh, ct);
            return Respond(ServiceResult<AddressesResult>.Ok(result,
                result.Addresses.Count == 0 ? "No saved addresses found." : $"{result.Addresses.Count} address(es) found."));
        }
        catch (ProviderNotConnectedException ex)
        {
            return Respond(ServiceResult<AddressesResult>.Fail(409, ex.Message));
        }
        catch (FoodProviderException ex)
        {
            return Respond(ServiceResult<AddressesResult>.Fail(ex.StatusCode, ex.Message));
        }
        catch (Exception ex) when (ex is McpException or HttpRequestException)
        {
            logger.LogWarning(ex, "Address sync failed for {Provider}", provider);
            return Respond(ServiceResult<AddressesResult>.Fail(502, "Couldn't load addresses from the provider right now."));
        }
    }

    [HttpPut("{provider}/addresses/default")]
    public async Task<ActionResult<ResponseCls<object>>> SetDefaultAddress(string provider, SetDefaultAddressRequest request, CancellationToken ct) =>
        await addresses.SetDefaultAsync(User.GetUserId(), provider, request.AddressId, ct)
            ? Respond(ServiceResult<object>.Ok(null!, "Default delivery address updated."))
            : Respond(ServiceResult<object>.Fail(404, "Address not found. Refresh your addresses and try again."));
}

public sealed class SetDefaultAddressRequest
{
    [Required] public string AddressId { get; set; } = string.Empty;
}
