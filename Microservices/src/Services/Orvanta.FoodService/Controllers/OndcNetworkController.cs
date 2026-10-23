using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orvanta.FoodService.Ondc;
using Orvanta.FoodService.Ondc.Crypto;
using Orvanta.FoodService.Ondc.Protocol;
using Orvanta.Shared.Auditing;

namespace Orvanta.FoodService.Controllers;

/// <summary>
/// The endpoints the ONDC network calls: sellers' callbacks ({SubscriberUrl}/on_search, …) and the two
/// registration checks (on_subscribe, site verification). Public, but every callback must carry a valid
/// ONDC signature (see OndcCallbackProcessor). Responses use ONDC's ACK/NACK format, not ResponseCls.
/// Every message is already recorded in OndcMessages, so the generic audit trail skips them.
/// </summary>
[ApiController]
[AllowAnonymous]
[SkipAudit]
public sealed class OndcNetworkController(OndcCallbackProcessor processor, OndcIdentity identity, ILogger<OndcNetworkController> logger)
    : ControllerBase
{
    // "action" is a reserved route value in MVC, hence {callback}.
    [HttpPost("ondc/{callback}")]
    public async Task<IActionResult> Callback(string callback, CancellationToken ct)
    {
        if (!OndcCallbackProcessor.Actions.Contains(callback))
        {
            return NotFound();
        }

        using var buffer = new MemoryStream();
        await Request.Body.CopyToAsync(buffer, ct);
        var result = await processor.HandleAsync(callback, buffer.ToArray(), Request.Headers.Authorization.FirstOrDefault(), ct);

        return result.Accepted ? Ok(Ack("ACK")) : StatusCode(result.StatusCode, Nack(result.StatusCode, result.Error));
    }

    /// <summary>
    /// Registration: ONDC sends a challenge encrypted for Orvanta's X25519 key; decrypting it proves Orvanta
    /// owns the subscriber URL and keys.
    /// </summary>
    [HttpPost("ondc/on_subscribe")]
    public IActionResult OnSubscribe([FromBody] JsonObject body)
    {
        var o = identity.Options;
        if (string.IsNullOrWhiteSpace(o.EncryptionPrivateKey) || string.IsNullOrWhiteSpace(o.OndcEncryptionPublicKey))
        {
            return StatusCode(503, new { error = "Ondc:EncryptionPrivateKey and Ondc:OndcEncryptionPublicKey must be configured." });
        }

        var challenge = OndcReader.Text(body["challenge"]);
        if (challenge is null)
        {
            return BadRequest(new { error = "Missing challenge." });
        }

        try
        {
            return Ok(new { answer = OndcCrypto.DecryptChallenge(o.EncryptionPrivateKey, o.OndcEncryptionPublicKey, challenge) });
        }
        catch (Exception ex) when (ex is FormatException or System.Security.Cryptography.CryptographicException or InvalidCastException)
        {
            logger.LogWarning(ex, "ONDC on_subscribe challenge could not be decrypted");
            return BadRequest(new { error = "Challenge could not be decrypted." });
        }
    }

    /// <summary>Registration: the page ONDC reads to check Orvanta controls its subscriber domain.</summary>
    [HttpGet("ondc-site-verification.html")]
    public IActionResult SiteVerification()
    {
        var requestId = identity.Options.SiteVerificationRequestId;
        if (string.IsNullOrWhiteSpace(requestId))
        {
            return NotFound();
        }

        var signed = OndcCrypto.Sign(identity.SigningKey, requestId);
        return Content($"<html><head><meta name='ondc-site-verification' content='{signed}' /></head>" +
                       "<body>ONDC Site Verification Page</body></html>", "text/html");
    }

    private static object Ack(string status) => new { message = new { ack = new { status } } };

    private static object Nack(int status, string? error) => new
    {
        message = new { ack = new { status = "NACK" } },
        error = new { type = status == 401 ? "POLICY-ERROR" : "JSON-SCHEMA-ERROR", code = status == 401 ? "10001" : "10000", message = error }
    };
}
