using System.Data;
using System.Text.Json;
using Dapper;
using Microsoft.Data.SqlClient;

namespace Orvanta.FoodService.Ondc.Data;

/// <summary>ONDC data access. Every call goes through a stored procedure in Database/Orvanta/StoredProcedures.</summary>
public interface IOndcRepository
{
    Task LogMessageAsync(OndcMessageLog message, CancellationToken ct);
    Task CreateSearchAsync(Guid transactionId, Guid userId, string query, string city, CancellationToken ct);
    Task MarkSearchFailedAsync(Guid transactionId, string error, CancellationToken ct);
    Task<int> AddCatalogItemsAsync(Guid transactionId, IReadOnlyList<OndcCatalogItem> items, CancellationToken ct);
    Task<IReadOnlyList<OndcCatalogItem>> ListCatalogAsync(Guid transactionId, Guid userId, CancellationToken ct);
    Task<OndcCatalogItem?> GetCatalogItemAsync(long catalogItemId, Guid userId, CancellationToken ct);
    Task CreateOrderAsync(OndcOrder order, CancellationToken ct);
    Task UpdateOrderAsync(Guid orderId, OndcOrderUpdate update, CancellationToken ct);
    Task<OndcOrder?> GetOrderAsync(Guid orderId, Guid userId, CancellationToken ct);
    Task<OndcOrder?> GetOrderByTransactionAsync(Guid transactionId, CancellationToken ct);
    Task<IReadOnlyList<OndcOrder>> ListOrdersAsync(Guid userId, int top, CancellationToken ct);
}

public sealed class OndcRepository(IConfiguration configuration) : IOndcRepository
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly string _connectionString = configuration.GetConnectionString("Orvanta")
        ?? throw new InvalidOperationException("Connection string 'Orvanta' is not configured.");

    public Task LogMessageAsync(OndcMessageLog m, CancellationToken ct) =>
        ExecuteAsync("dbo.usp_OndcMessages_Insert", new
        {
            m.Direction, m.Action, m.TransactionId, m.MessageId, m.Counterparty, m.PayloadJson, m.SignatureValid, m.HttpStatus, m.Error
        }, ct);

    public Task CreateSearchAsync(Guid transactionId, Guid userId, string query, string city, CancellationToken ct) =>
        ExecuteAsync("dbo.usp_OndcSearches_Create", new { TransactionId = transactionId, UserId = userId, Query = query, City = city }, ct);

    public Task MarkSearchFailedAsync(Guid transactionId, string error, CancellationToken ct) =>
        ExecuteAsync("dbo.usp_OndcSearches_MarkFailed", new { TransactionId = transactionId, Error = error }, ct);

    public async Task<int> AddCatalogItemsAsync(Guid transactionId, IReadOnlyList<OndcCatalogItem> items, CancellationToken ct)
    {
        if (items.Count == 0) return 0;
        await using var connection = new SqlConnection(_connectionString);
        return await connection.ExecuteScalarAsync<int>(Procedure("dbo.usp_OndcCatalogItems_AddFromJson",
            new { TransactionId = transactionId, ItemsJson = JsonSerializer.Serialize(items, Json) }, ct));
    }

    public Task<IReadOnlyList<OndcCatalogItem>> ListCatalogAsync(Guid transactionId, Guid userId, CancellationToken ct) =>
        QueryAsync<OndcCatalogItem>("dbo.usp_OndcCatalogItems_ListBySearch", new { TransactionId = transactionId, UserId = userId }, ct);

    public async Task<OndcCatalogItem?> GetCatalogItemAsync(long catalogItemId, Guid userId, CancellationToken ct) =>
        (await QueryAsync<OndcCatalogItem>("dbo.usp_OndcCatalogItems_Get", new { CatalogItemId = catalogItemId, UserId = userId }, ct)).FirstOrDefault();

    public Task CreateOrderAsync(OndcOrder o, CancellationToken ct) =>
        ExecuteAsync("dbo.usp_OndcOrders_Create", new
        {
            o.OrderId, o.UserId, o.TransactionId, o.BppId, o.BppUri, o.ProviderId, o.ProviderName, o.LocationId,
            o.ItemId, o.ItemName, o.City, o.Quantity, o.Currency
        }, ct);

    public Task UpdateOrderAsync(Guid orderId, OndcOrderUpdate u, CancellationToken ct) =>
        ExecuteAsync("dbo.usp_OndcOrders_Update", new
        {
            OrderId = orderId, u.State, u.QuoteTotal, u.Currency, u.QuoteBreakupJson, u.DeliveryJson, u.BppOrderId, u.Error
        }, ct);

    public async Task<OndcOrder?> GetOrderAsync(Guid orderId, Guid userId, CancellationToken ct) =>
        (await QueryAsync<OndcOrder>("dbo.usp_OndcOrders_Get", new { OrderId = orderId, UserId = userId }, ct)).FirstOrDefault();

    public async Task<OndcOrder?> GetOrderByTransactionAsync(Guid transactionId, CancellationToken ct) =>
        (await QueryAsync<OndcOrder>("dbo.usp_OndcOrders_Get", new { TransactionId = transactionId }, ct)).FirstOrDefault();

    public Task<IReadOnlyList<OndcOrder>> ListOrdersAsync(Guid userId, int top, CancellationToken ct) =>
        QueryAsync<OndcOrder>("dbo.usp_OndcOrders_ListByUser", new { UserId = userId, Top = top }, ct);

    private async Task ExecuteAsync(string procedure, object parameters, CancellationToken ct)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.ExecuteAsync(Procedure(procedure, parameters, ct));
    }

    private async Task<IReadOnlyList<T>> QueryAsync<T>(string procedure, object parameters, CancellationToken ct)
    {
        await using var connection = new SqlConnection(_connectionString);
        return (await connection.QueryAsync<T>(Procedure(procedure, parameters, ct))).AsList();
    }

    private static CommandDefinition Procedure(string name, object parameters, CancellationToken ct) =>
        new(name, parameters, commandType: CommandType.StoredProcedure, cancellationToken: ct);
}
