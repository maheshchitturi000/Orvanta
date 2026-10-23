using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Orvanta.FoodService.Ondc.Crypto;
using Orvanta.FoodService.Ondc.Data;
using Orvanta.FoodService.Ondc.Network;
using Orvanta.FoodService.Ondc.Protocol;

namespace Orvanta.FoodService.Ondc;

/// <summary>Outcome of a callback: ACK, or NACK with the HTTP status and reason to send back.</summary>
public sealed record OndcCallbackResult(bool Accepted, int StatusCode, string? Error)
{
    public static readonly OndcCallbackResult Ack = new(true, 200, null);
    public static OndcCallbackResult Nack(int status, string error) => new(false, status, error);
}

/// <summary>
/// Handles sellers' asynchronous replies (on_search, on_select, on_init, on_confirm, on_status): verifies the
/// signature against the registry, logs the message, stores what it says, and wakes the waiting request.
/// </summary>
public sealed class OndcCallbackProcessor(
    OndcIdentity identity,
    OndcRegistry registry,
    IOndcRepository repository,
    OndcCallbackSignals signals,
    ILogger<OndcCallbackProcessor> logger)
{
    public static readonly string[] Actions = ["on_search", "on_select", "on_init", "on_confirm", "on_status", "on_cancel", "on_update", "on_track", "on_support"];

    public async Task<OndcCallbackResult> HandleAsync(string action, byte[] body, string? authorization, CancellationToken ct)
    {
        var json = Encoding.UTF8.GetString(body);
        OndcEnvelope? envelope;
        try
        {
            envelope = OndcReader.Envelope(JsonNode.Parse(json));
        }
        catch (JsonException)
        {
            envelope = null;
        }

        if (envelope is null || envelope.Action != action)
        {
            await Log(action, envelope, json, signatureValid: null, "Invalid request body.", ct);
            return OndcCallbackResult.Nack(400, "Invalid request body.");
        }

        // Unsigned counts as unverified: when verification is on, only a valid signature is accepted.
        var signatureValid = await VerifyAsync(body, authorization, ct);
        if (identity.Options.VerifyCallbackSignatures && signatureValid != true)
        {
            var reason = signatureValid is null ? "Missing Authorization signature." : "Signature verification failed.";
            await Log(action, envelope, json, signatureValid, reason, ct);
            return OndcCallbackResult.Nack(401, reason);
        }

        await Log(action, envelope, json, signatureValid, envelope.Error, ct);

        if (!Guid.TryParse(envelope.TransactionId, out var transactionId))
        {
            return OndcCallbackResult.Ack; // not one of ours; logged above
        }

        try
        {
            await ApplyAsync(action, transactionId, envelope, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The message is valid and logged; a processing problem must not make the seller retry forever.
            logger.LogError(ex, "Processing ONDC {Action} for transaction {TransactionId} failed", action, transactionId);
        }
        finally
        {
            signals.Signal(envelope.TransactionId, action);
        }
        return OndcCallbackResult.Ack;
    }

    private async Task ApplyAsync(string action, Guid transactionId, OndcEnvelope envelope, CancellationToken ct)
    {
        if (action == "on_search")
        {
            var added = await repository.AddCatalogItemsAsync(transactionId, OndcReader.CatalogItems(envelope), ct);
            logger.LogInformation("ONDC on_search from {Bpp}: {Count} new items", envelope.BppId, added);
            return;
        }

        var order = await repository.GetOrderByTransactionAsync(transactionId, ct);
        if (order is null)
        {
            return;
        }

        if (envelope.Error is not null)
        {
            await repository.UpdateOrderAsync(order.OrderId, new OndcOrderUpdate(OndcOrderStates.Failed, Error: envelope.Error), ct);
            return;
        }

        switch (action)
        {
            case "on_select":
            {
                var quote = OndcReader.Quote(envelope);
                await repository.UpdateOrderAsync(order.OrderId, quote is null
                    ? new OndcOrderUpdate(OndcOrderStates.Failed, Error: "The restaurant did not send a price.")
                    : new OndcOrderUpdate(OndcOrderStates.Quoted, quote.Total, quote.Currency, quote.BreakupJson), ct);
                break;
            }
            case "on_init":
            {
                var quote = OndcReader.Quote(envelope);
                await repository.UpdateOrderAsync(order.OrderId,
                    new OndcOrderUpdate(OndcOrderStates.Initialized, quote?.Total, quote?.Currency, quote?.BreakupJson), ct);
                break;
            }
            case "on_confirm":
            case "on_status":
            {
                var (bppOrderId, state) = OndcReader.OrderState(envelope);
                // "Created" from the seller means our confirm went through.
                var newState = state is null or "Created" ? OndcOrderStates.Confirmed : state;
                await repository.UpdateOrderAsync(order.OrderId, new OndcOrderUpdate(newState, BppOrderId: bppOrderId), ct);
                break;
            }
        }
    }

    /// <summary>True/false when a signature was checked; null when none was sent.</summary>
    private async Task<bool?> VerifyAsync(byte[] body, string? authorization, CancellationToken ct)
    {
        var header = OndcAuthHeader.Parse(authorization);
        if (header is null)
        {
            return authorization is null ? null : false;
        }

        var publicKey = await registry.SigningPublicKeyAsync(header.SubscriberId, header.UniqueKeyId, ct);
        return publicKey is not null && header.IsValid(publicKey, body);
    }

    private Task Log(string action, OndcEnvelope? envelope, string json, bool? signatureValid, string? error, CancellationToken ct) =>
        repository.LogMessageAsync(new OndcMessageLog("In", action, envelope?.TransactionId, envelope?.MessageId,
            envelope?.BppId ?? envelope?.BppUri, json, signatureValid, Error: error), ct);
}
