
using Dapper;
using Microsoft.Data.SqlClient;
using Orvanta.IntegrationService.Models;
using System.Data;

namespace Orvanta.IntegrationService.Repositories;

public sealed class ExternalIntegrationRepository
    : IExternalIntegrationRepository
{
    private readonly string _connectionString;

    public ExternalIntegrationRepository(IConfiguration configuration)
    {
        _connectionString =
            configuration.GetConnectionString("OrvantaDb")
            ?? throw new InvalidOperationException(
                "OrvantaDb connection string is missing.");
    }

    public async Task<bool> SaveIntegrationAsync(
        string integrationName,
        string provider,
        bool isConnected,
        string integrationDataJson,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(_connectionString);

        var result = await connection.ExecuteAsync(
            new CommandDefinition(
                "dbo.usp_ExternalIntegrations_Upsert",
                new
                {
                    IntegrationName = integrationName,
                    Provider = provider,
                    IsConnected = isConnected,
                    IntegrationDataJson = integrationDataJson
                },
                commandType: CommandType.StoredProcedure,
                cancellationToken: cancellationToken));

        return result >= 0;
    }

    public async Task<IntegrationRecord?> GetIntegrationAsync(
        string provider,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(_connectionString);

        return await connection.QuerySingleOrDefaultAsync<IntegrationRecord>(
            new CommandDefinition(
                "dbo.usp_ExternalIntegrations_GetByProvider",
                new { Provider = provider },
                commandType: CommandType.StoredProcedure,
                cancellationToken: cancellationToken));
    }

    public async Task<IntegrationStatusDto?> GetIntegrationStatusAsync(
        string provider,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(_connectionString);

        return await connection.QuerySingleOrDefaultAsync<IntegrationStatusDto>(
            new CommandDefinition(
                "dbo.usp_ExternalIntegrations_GetStatusByProvider",
                new { Provider = provider },
                commandType: CommandType.StoredProcedure,
                cancellationToken: cancellationToken));
    }


    public async Task<IEnumerable<SavedIntegrationAddress>>
        GetSavedAddressesAsync(
            string provider,
            string userId,
            CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(_connectionString);

        return await connection.QueryAsync<SavedIntegrationAddress>(
            new CommandDefinition(
                "dbo.usp_IntegrationAddresses_GetByUser",
                new { Provider = provider, UserId = userId },
                commandType: CommandType.StoredProcedure,
                cancellationToken: cancellationToken));
    }

    public async Task SaveAddressAsync(
        string provider,
        string externalAddressId,
        string userId,
        string? label,
        string? phoneNumber,
        string? addressLine,
        bool isDefault,
        string rawAddressJson,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(_connectionString);

        await connection.ExecuteAsync(
            new CommandDefinition(
                "dbo.usp_IntegrationAddresses_Upsert",
                new
                {
                    Provider = provider,
                    ExternalAddressId = externalAddressId,
                    UserId = userId,
                    Label = label,
                    PhoneNumber = phoneNumber,
                    AddressLine = addressLine,
                    IsDefault = isDefault,
                    RawAddressJson = rawAddressJson
                },
                commandType: CommandType.StoredProcedure,
                cancellationToken: cancellationToken));
    }

    public async Task<IEnumerable<ExternalIntegrationDto>> GetAllAsync(
    CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(_connectionString);

        var integrations = await connection
            .QueryAsync<ExternalIntegrationDto>(
                new CommandDefinition(
                    "dbo.usp_ExternalIntegrations_GetAll",
                    commandType: CommandType.StoredProcedure,
                    cancellationToken: cancellationToken));

        return integrations;
    }
}
