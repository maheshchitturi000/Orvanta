
using Orvanta.IntegrationService.Models;

namespace Orvanta.IntegrationService.Repositories;

public interface IExternalIntegrationRepository
{
    Task<bool> SaveIntegrationAsync(
        string integrationName,
        string provider,
        bool isConnected,
        string integrationDataJson,
        CancellationToken cancellationToken = default);

    Task<IntegrationRecord?> GetIntegrationAsync(
        string provider,
        CancellationToken cancellationToken = default);

    Task<IntegrationStatusDto?> GetIntegrationStatusAsync(
        string provider,
        CancellationToken cancellationToken = default);

    Task<IEnumerable<SavedIntegrationAddress>> GetSavedAddressesAsync(
        string provider,
        string userId,
        CancellationToken cancellationToken = default);

    Task<IEnumerable<ExternalIntegrationDto>> GetAllAsync(
    CancellationToken cancellationToken = default);
    Task SaveAddressAsync(
        string provider,
        string externalAddressId,
        string userId,
        string? label,
        string? phoneNumber,
        string? addressLine,
        bool isDefault,
        string rawAddressJson,
        CancellationToken cancellationToken = default);
}

public sealed record IntegrationRecord(
    string IntegrationName,
    string Provider,
    bool IsConnected,
    string? IntegrationDataJson);

public sealed record IntegrationStatusDto(
    string? IntegrationName,
    string? Provider,
    bool IsConnected,
    DateTime? ConnectedAt);

public sealed record SavedIntegrationAddress(
    string Id,
    string? Label,
    string? PhoneNumber,
    string? AddressLine,
    bool IsDefault);

