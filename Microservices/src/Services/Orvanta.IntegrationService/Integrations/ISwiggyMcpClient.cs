
namespace Orvanta.IntegrationService.Integrations;

public interface ISwiggyMcpClient
{
    Task<IReadOnlyList<SwiggyRemoteAddress>> GetAllAddressesAsync(
        string accessToken,
        CancellationToken cancellationToken = default);
}

public sealed record SwiggyRemoteAddress(
    string Id,
    string? AddressLine,
    string? PhoneNumber,
    string? Label,
    string RawJson);
