using Microsoft.EntityFrameworkCore;
using Orvanta.FoodService.Data;
using Orvanta.FoodService.Domain;
using Orvanta.FoodService.Integrations;
using Orvanta.FoodService.Providers;

namespace Orvanta.FoodService.Services;

public sealed record AddressDto(string Id, string? Label, string? AddressLine, string? PhoneNumber, bool IsDefault);

public sealed record AddressesResult(string Provider, string Source, IReadOnlyList<AddressDto> Addresses);

/// <summary>Database-first address book per provider; syncs from the provider on demand.</summary>
public sealed class AddressService(FoodDbContext db, ProviderConnectionService connections, FoodProviderRegistry registry)
{
    public async Task<AddressesResult> GetAsync(Guid userId, string provider, bool refresh, CancellationToken ct)
    {
        provider = provider.ToLowerInvariant();

        if (!refresh)
        {
            var cached = await LoadAsync(userId, provider, ct);
            if (cached.Count > 0)
            {
                return new AddressesResult(provider, "database", cached);
            }
        }

        var foodProvider = registry.Get(provider) ?? throw new FoodProviderException("This integration is not available.", 404);
        var remote = await connections.WithSessionAsync(userId, provider, s => foodProvider.GetAddressesAsync(s, ct), ct);

        var existing = await db.SavedAddresses.Where(a => a.UserId == userId && a.Provider == provider).ToListAsync(ct);
        var preferredId = existing.FirstOrDefault(a => a.IsDefault)?.ExternalId;
        if (preferredId is null || remote.All(r => r.Id != preferredId))
        {
            preferredId = remote.FirstOrDefault()?.Id;
        }

        db.SavedAddresses.RemoveRange(existing);
        db.SavedAddresses.AddRange(remote.Select(r => new SavedAddress
        {
            UserId = userId,
            Provider = provider,
            ExternalId = r.Id,
            Label = Trim(r.Label, 100),
            AddressLine = Trim(r.AddressLine, 500),
            PhoneNumber = Trim(r.PhoneNumber, 20),
            IsDefault = r.Id == preferredId
        }));
        await db.SaveChangesAsync(ct);

        return new AddressesResult(provider, provider, await LoadAsync(userId, provider, ct));
    }

    public async Task<bool> SetDefaultAsync(Guid userId, string provider, string addressId, CancellationToken ct)
    {
        provider = provider.ToLowerInvariant();
        var addresses = await db.SavedAddresses.Where(a => a.UserId == userId && a.Provider == provider).ToListAsync(ct);
        if (addresses.All(a => a.ExternalId != addressId))
        {
            return false;
        }

        addresses.ForEach(a => a.IsDefault = a.ExternalId == addressId);
        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>The address to use for search/ordering: the default one, syncing from the provider if needed.</summary>
    public async Task<string?> GetDefaultAddressIdAsync(Guid userId, string provider, CancellationToken ct)
    {
        var result = await GetAsync(userId, provider, refresh: false, ct);
        return result.Addresses.FirstOrDefault(a => a.IsDefault)?.Id ?? result.Addresses.FirstOrDefault()?.Id;
    }

    private async Task<List<AddressDto>> LoadAsync(Guid userId, string provider, CancellationToken ct) =>
        await db.SavedAddresses.AsNoTracking()
            .Where(a => a.UserId == userId && a.Provider == provider)
            .OrderByDescending(a => a.IsDefault).ThenBy(a => a.Label)
            .Select(a => new AddressDto(a.ExternalId, a.Label, a.AddressLine, a.PhoneNumber, a.IsDefault))
            .ToListAsync(ct);

    private static string? Trim(string? value, int max) => value is null || value.Length <= max ? value : value[..max];
}
