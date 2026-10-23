using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Orvanta.FoodService.Ondc;
using Orvanta.FoodService.Ondc.Crypto;
using Orvanta.FoodService.Ondc.Data;
using Orvanta.FoodService.Ondc.Network;
using Orvanta.FoodService.Ondc.Protocol;
using Orvanta.FoodService.Ondc.Sandbox;

namespace Orvanta.UnitTests;

public class OndcTests
{
    private static readonly byte[] Body = Encoding.UTF8.GetBytes("""{"context":{"action":"search"}}""");

    [Fact]
    public void Blake512_matches_the_reference_digest()
    {
        // BLAKE2b-512 of the empty input (RFC 7693 test vectors).
        var expected = Convert.ToBase64String(Convert.FromHexString(
            "786a02f742015903c6c6fd852552d272912f4740e15847618a86e217f71f5419d25e1031afee585313896444934eb04b903a685b1448b755d56f701afe9be2ce"));

        Assert.Equal(expected, OndcCrypto.Blake512Base64([]));
    }

    [Fact]
    public void Signed_header_verifies_for_the_same_body()
    {
        var key = OndcCrypto.GenerateSigningKey();
        var header = OndcAuthHeader.Parse(OndcAuthHeader.Create("buyer.test", "k1", key, Body, TimeSpan.FromMinutes(5)));

        Assert.NotNull(header);
        Assert.Equal("buyer.test", header.SubscriberId);
        Assert.Equal("k1", header.UniqueKeyId);
        Assert.True(header.IsValid(key.PublicKeyBase64, Body));
    }

    [Fact]
    public void Signature_fails_for_a_changed_body_another_key_or_after_expiry()
    {
        var key = OndcCrypto.GenerateSigningKey();
        var now = DateTimeOffset.UtcNow;
        var header = OndcAuthHeader.Parse(OndcAuthHeader.Create("buyer.test", "k1", key, Body, TimeSpan.FromMinutes(5), now))!;

        Assert.False(header.IsValid(key.PublicKeyBase64, Encoding.UTF8.GetBytes("""{"context":{"action":"select"}}"""), now));
        Assert.False(header.IsValid(OndcCrypto.GenerateSigningKey().PublicKeyBase64, Body, now));
        Assert.False(header.IsValid(key.PublicKeyBase64, Body, now.AddMinutes(10)));
    }

    [Fact]
    public void Signing_key_loads_from_seed_or_libsodium_form()
    {
        var key = OndcCrypto.GenerateSigningKey();
        var libsodium = Convert.ToBase64String([.. key.PrivateSeed, .. key.PublicKey]);

        Assert.Equal(key.PublicKeyBase64, OndcCrypto.LoadSigningKey(Convert.ToBase64String(key.PrivateSeed)).PublicKeyBase64);
        Assert.Equal(key.PublicKeyBase64, OndcCrypto.LoadSigningKey(libsodium).PublicKeyBase64);
    }

    [Fact]
    public void Reads_catalog_items_from_on_search()
    {
        var envelope = OndcReader.Envelope(JsonNode.Parse("""
        {
          "context": { "action": "on_search", "transaction_id": "t1", "message_id": "m1", "bpp_id": "seller.test", "bpp_uri": "https://seller.test/ondc" },
          "message": { "catalog": { "bpp/providers": [ {
            "id": "p1", "descriptor": { "name": "Test Kitchen" }, "locations": [ { "id": "l1" } ],
            "items": [
              { "id": "i1", "descriptor": { "name": "Masala Dosa", "images": ["https://img.test/dosa.jpg"] },
                "price": { "currency": "INR", "value": "90.00" },
                "tags": [ { "code": "veg_nonveg", "list": [ { "code": "veg", "value": "yes" } ] } ] },
              { "id": "i2", "descriptor": { "name": "Chicken 65" }, "price": { "value": 260 },
                "tags": [ { "code": "veg_nonveg", "list": [ { "code": "non_veg", "value": "yes" } ] } ] },
              { "id": "i3" }
            ] } ] } }
        }
        """))!;

        var items = OndcReader.CatalogItems(envelope);

        Assert.Equal(2, items.Count);
        Assert.Equal(("p1", "Test Kitchen", "l1"), (items[0].ProviderId, items[0].ProviderName, items[0].LocationId));
        Assert.Equal((90.00m, true, "https://img.test/dosa.jpg"), (items[0].Price!.Value, items[0].IsVeg!.Value, items[0].ImageUrl));
        Assert.Equal((260m, false), (items[1].Price!.Value, items[1].IsVeg!.Value));
    }

    [Fact]
    public void Reads_quote_and_seller_error()
    {
        var quoted = OndcReader.Envelope(JsonNode.Parse("""
        { "context": { "action": "on_select", "transaction_id": "t1" },
          "message": { "order": { "quote": { "price": { "currency": "INR", "value": "139.50" }, "breakup": [
            { "title": "Masala Dosa", "@ondc/org/title_type": "item", "price": { "value": "90.00" } },
            { "title": "Delivery charges", "@ondc/org/title_type": "delivery", "price": { "value": "30" } } ] } } } }
        """))!;
        var failed = OndcReader.Envelope(JsonNode.Parse("""
        { "context": { "action": "on_select", "transaction_id": "t1" }, "error": { "code": "40002", "message": "Item not available" } }
        """))!;

        var quote = OndcReader.Quote(quoted)!;

        Assert.Equal(139.50m, quote.Total);
        Assert.Equal(["item", "delivery"], quote.Lines.Select(l => l.Type));
        Assert.Null(quoted.Error);
        Assert.Equal("40002: Item not available", failed.Error);
    }

    [Fact]
    public void Sandbox_catalog_matches_dish_or_category()
    {
        Assert.Contains(OndcSandboxCatalog.Search("biryani"), m => m.Dishes.Any(d => d.Name == "Paneer Biryani"));
        Assert.Single(OndcSandboxCatalog.Search("dosa"));
        Assert.Empty(OndcSandboxCatalog.Search("sushi"));
    }

    [Fact]
    public async Task Callback_without_signature_is_rejected_and_not_stored()
    {
        var (processor, repository, _) = CreateProcessor();

        var result = await processor.HandleAsync("on_search", OnSearchBody(), authorization: null, CancellationToken.None);

        Assert.False(result.Accepted);
        Assert.Equal(401, result.StatusCode);
        Assert.Empty(repository.StoredItems);
        Assert.Null(repository.Logged.Single().SignatureValid);
    }

    [Fact]
    public async Task Callback_signed_with_an_unregistered_key_is_rejected()
    {
        var (processor, repository, _) = CreateProcessor();
        var body = OnSearchBody();
        var forged = OndcAuthHeader.Create("seller.test", "k1", OndcCrypto.GenerateSigningKey(), body, TimeSpan.FromMinutes(5));

        var result = await processor.HandleAsync("on_search", body, forged, CancellationToken.None);

        Assert.Equal(401, result.StatusCode);
        Assert.Empty(repository.StoredItems);
    }

    [Fact]
    public async Task Callback_signed_by_a_registered_seller_is_stored()
    {
        var (processor, repository, sellerKey) = CreateProcessor();
        var body = OnSearchBody();
        var header = OndcAuthHeader.Create("seller.test", "k1", sellerKey, body, TimeSpan.FromMinutes(5));

        var result = await processor.HandleAsync("on_search", body, header, CancellationToken.None);

        Assert.True(result.Accepted);
        Assert.Equal("Masala Dosa", Assert.Single(repository.StoredItems).ItemName);
        Assert.True(repository.Logged.Single().SignatureValid);
    }

    private static byte[] OnSearchBody() => Encoding.UTF8.GetBytes($$"""
        { "context": { "action": "on_search", "transaction_id": "{{Guid.NewGuid()}}", "message_id": "m1", "bpp_id": "seller.test", "bpp_uri": "https://seller.test/ondc" },
          "message": { "catalog": { "bpp/providers": [ { "id": "p1", "descriptor": { "name": "Test Kitchen" },
            "items": [ { "id": "i1", "descriptor": { "name": "Masala Dosa" }, "price": { "value": "90" } } ] } ] } } }
        """);

    /// <summary>A processor whose registry knows one seller ("seller.test" / "k1") with the returned key.</summary>
    private static (OndcCallbackProcessor Processor, FakeRepository Repository, OndcSigningKey SellerKey) CreateProcessor()
    {
        var sellerKey = OndcCrypto.GenerateSigningKey();
        var identity = new OndcIdentity(Options.Create(new OndcOptions { Mode = OndcMode.Sandbox }), new ConfigurationBuilder().Build());
        var registryReply = new JsonArray(new JsonObject
        {
            ["subscriber_id"] = "seller.test",
            ["ukId"] = "k1",
            ["signing_public_key"] = sellerKey.PublicKeyBase64
        }).ToJsonString();

        var registry = new OndcRegistry(identity, new StubHttpClientFactory(registryReply),
            new MemoryCache(new MemoryCacheOptions()), NullLogger<OndcRegistry>.Instance);
        var repository = new FakeRepository();
        var processor = new OndcCallbackProcessor(identity, registry, repository, new OndcCallbackSignals(), NullLogger<OndcCallbackProcessor>.Instance);
        return (processor, repository, sellerKey);
    }

    private sealed class StubHttpClientFactory(string reply) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new StubHandler(reply));

        private sealed class StubHandler(string reply) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
                Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(reply, Encoding.UTF8, "application/json") });
        }
    }

    private sealed class FakeRepository : IOndcRepository
    {
        public List<OndcMessageLog> Logged { get; } = [];
        public List<OndcCatalogItem> StoredItems { get; } = [];

        public Task LogMessageAsync(OndcMessageLog message, CancellationToken ct) { Logged.Add(message); return Task.CompletedTask; }

        public Task<int> AddCatalogItemsAsync(Guid transactionId, IReadOnlyList<OndcCatalogItem> items, CancellationToken ct)
        {
            StoredItems.AddRange(items);
            return Task.FromResult(items.Count);
        }

        public Task CreateSearchAsync(Guid transactionId, Guid userId, string query, string city, CancellationToken ct) => Task.CompletedTask;
        public Task MarkSearchFailedAsync(Guid transactionId, string error, CancellationToken ct) => Task.CompletedTask;
        public Task<IReadOnlyList<OndcCatalogItem>> ListCatalogAsync(Guid transactionId, Guid userId, CancellationToken ct) => Task.FromResult<IReadOnlyList<OndcCatalogItem>>(StoredItems);
        public Task<OndcCatalogItem?> GetCatalogItemAsync(long catalogItemId, Guid userId, CancellationToken ct) => Task.FromResult<OndcCatalogItem?>(null);
        public Task CreateOrderAsync(OndcOrder order, CancellationToken ct) => Task.CompletedTask;
        public Task UpdateOrderAsync(Guid orderId, OndcOrderUpdate update, CancellationToken ct) => Task.CompletedTask;
        public Task<OndcOrder?> GetOrderAsync(Guid orderId, Guid userId, CancellationToken ct) => Task.FromResult<OndcOrder?>(null);
        public Task<OndcOrder?> GetOrderByTransactionAsync(Guid transactionId, CancellationToken ct) => Task.FromResult<OndcOrder?>(null);
        public Task<IReadOnlyList<OndcOrder>> ListOrdersAsync(Guid userId, int top, CancellationToken ct) => Task.FromResult<IReadOnlyList<OndcOrder>>([]);
    }
}
