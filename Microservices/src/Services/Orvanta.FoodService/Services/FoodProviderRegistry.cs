using Orvanta.FoodService.Providers;

namespace Orvanta.FoodService.Services;

/// <summary>Resolves an <see cref="IFoodProvider"/> by key. Providers are registered in DI.</summary>
public sealed class FoodProviderRegistry(IEnumerable<IFoodProvider> providers)
{
    private readonly Dictionary<string, IFoodProvider> _providers =
        providers.ToDictionary(p => p.Key, StringComparer.OrdinalIgnoreCase);

    public IFoodProvider? Get(string key) => _providers.GetValueOrDefault(key);

    public bool Has(string key) => _providers.ContainsKey(key);
}
