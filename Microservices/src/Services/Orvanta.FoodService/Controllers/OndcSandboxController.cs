using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orvanta.FoodService.Ondc;
using Orvanta.FoodService.Ondc.Crypto;
using Orvanta.FoodService.Ondc.Protocol;
using Orvanta.FoodService.Ondc.Sandbox;
using Orvanta.Shared.Auditing;

namespace Orvanta.FoodService.Controllers;

/// <summary>
/// The local ONDC sandbox (gateway, seller, registry) — see <see cref="OndcSandboxNetwork"/>. Every endpoint
/// answers 404 unless Ondc:Mode is Sandbox, so it never exists in staging or production.
/// </summary>
[ApiController]
[AllowAnonymous]
[SkipAudit]
[Route("ondc-sandbox")]
public sealed class OndcSandboxController(OndcSandboxNetwork network, OndcIdentity identity) : ControllerBase
{
    private static readonly string[] SellerActions = ["select", "init", "confirm", "status"];

    [HttpPost("gateway/search")]
    public Task<IActionResult> Search(CancellationToken ct) => ReceiveAsync("search", ct);

    [HttpPost("seller/{sellerAction}")]
    public Task<IActionResult> Seller(string sellerAction, CancellationToken ct) =>
        SellerActions.Contains(sellerAction) ? ReceiveAsync(sellerAction, ct) : Task.FromResult<IActionResult>(NotFound());

    [HttpPost("registry/lookup")]
    public async Task<IActionResult> Lookup(CancellationToken ct)
    {
        if (!identity.Options.IsSandbox) return NotFound();

        var body = await JsonNode.ParseAsync(Request.Body, cancellationToken: ct);
        return Ok(network.Lookup(OndcReader.Text(body?["subscriber_id"]), OndcReader.Text(body?["ukId"])));
    }

    /// <summary>New signing and encryption key pairs, ready to put in user-secrets before registering with ONDC.</summary>
    [HttpGet("keys")]
    public IActionResult Keys() => identity.Options.IsSandbox ? Ok(OndcCrypto.GenerateRegistrationKeys()) : NotFound();

    private async Task<IActionResult> ReceiveAsync(string action, CancellationToken ct)
    {
        if (!identity.Options.IsSandbox) return NotFound();

        using var buffer = new MemoryStream();
        await Request.Body.CopyToAsync(buffer, ct);
        var bytes = buffer.ToArray();

        if (!network.IsSignedByBuyer(bytes, Request.Headers.Authorization.FirstOrDefault()))
        {
            return Unauthorized(Nack("10001", "Signature verification failed."));
        }

        JsonObject? request;
        try
        {
            request = JsonNode.Parse(bytes) as JsonObject;
        }
        catch (JsonException)
        {
            request = null;
        }
        if (request?["context"] is not JsonObject context || OndcReader.Text(context["action"]) != action)
        {
            return BadRequest(Nack("10000", "Invalid request body."));
        }

        network.Reply(action, request);
        return Ok(new { message = new { ack = new { status = "ACK" } } });
    }

    private static object Nack(string code, string message) => new
    {
        message = new { ack = new { status = "NACK" } },
        error = new { type = "POLICY-ERROR", code, message }
    };
}
