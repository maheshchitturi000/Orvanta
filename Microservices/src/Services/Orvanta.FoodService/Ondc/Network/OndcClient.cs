using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Orvanta.FoodService.Ondc.Crypto;
using Orvanta.FoodService.Ondc.Data;
using Orvanta.FoodService.Ondc.Protocol;

namespace Orvanta.FoodService.Ondc.Network;

/// <summary>A participant's immediate answer: ACK (it will reply via callback) or NACK with a reason.</summary>
public sealed record OndcAck(bool Accepted, string? Error)
{
    public static OndcAck Nack(string error) => new(false, error);
}

/// <summary>
/// Sends signed requests to the gateway (search) or a seller (select, init, confirm, status) and logs each one.
/// The exact bytes that are signed are the bytes that are sent.
/// </summary>
public sealed class OndcClient(
    OndcIdentity identity,
    IHttpClientFactory httpClientFactory,
    IOndcRepository repository,
    ILogger<OndcClient> logger)
{
    /// <summary>Search goes to the gateway, which forwards it to every matching seller.</summary>
    public Task<OndcAck> SearchAsync(JsonObject body, CancellationToken ct) =>
        SendAsync($"{identity.GatewayUrl}/search", "search", body, identity.GatewayUrl, ct);

    /// <summary>Everything after search goes straight to the seller that offered the item.</summary>
    public Task<OndcAck> SendToSellerAsync(string bppUri, string action, JsonObject body, CancellationToken ct) =>
        SendAsync($"{bppUri.TrimEnd('/')}/{action}", action, body, bppUri, ct);

    private async Task<OndcAck> SendAsync(string url, string action, JsonObject body, string counterparty, CancellationToken ct)
    {
        var json = body.ToJsonString();
        var bytes = Encoding.UTF8.GetBytes(json);
        var context = body["context"];
        var transactionId = OndcReader.Text(context?["transaction_id"]);
        var messageId = OndcReader.Text(context?["message_id"]);

        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new ByteArrayContent(bytes) { Headers = { { "Content-Type", "application/json" } } }
        };
        request.Headers.TryAddWithoutValidation("Authorization", OndcAuthHeader.Create(identity.SubscriberId, identity.UniqueKeyId,
            identity.SigningKey, bytes, TimeSpan.FromSeconds(identity.Options.SignatureValiditySeconds)));

        OndcAck ack;
        int? status = null;
        try
        {
            using var response = await httpClientFactory.CreateClient("ondc").SendAsync(request, ct);
            status = (int)response.StatusCode;
            ack = ReadAck(await response.Content.ReadAsStringAsync(ct), response.IsSuccessStatusCode);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "ONDC {Action} to {Url} failed", action, url);
            ack = OndcAck.Nack($"Could not reach {new Uri(url).Host}.");
        }

        await repository.LogMessageAsync(new OndcMessageLog("Out", action, transactionId, messageId, counterparty, json,
            HttpStatus: status, Error: ack.Error), ct);
        return ack;
    }

    /// <summary>{"message":{"ack":{"status":"ACK"}}} or NACK with {"error":{...}}.</summary>
    private static OndcAck ReadAck(string body, bool httpOk)
    {
        try
        {
            var root = JsonNode.Parse(body);
            var status = OndcReader.Text(root?["message"]?["ack"]?["status"]);
            if (status == "ACK" && httpOk)
            {
                return new OndcAck(true, null);
            }
            var error = root?["error"];
            return OndcAck.Nack(OndcReader.Text(error?["message"]) ?? OndcReader.Text(error?["code"]) ?? "The request was not accepted.");
        }
        catch (JsonException)
        {
            return OndcAck.Nack(httpOk ? "Unexpected reply." : "The request was not accepted.");
        }
    }
}
