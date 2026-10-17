using Microsoft.Extensions.Options;
using Orvanta.FoodService.Integrations;
using Orvanta.FoodService.Integrations.Mcp;
using Orvanta.FoodService.Options;
using Orvanta.FoodService.Providers;

namespace Orvanta.FoodService.Services;

public sealed record RankedFoodItem(int Rank, double Score, FoodItem Item);

public sealed record ProviderIssue(string Provider, string Message, bool NeedsConnection);

public sealed record FoodAssistResult(
    bool IsFoodIntent,
    string Query,
    bool VegOnly,
    bool AutoPickTop,
    IReadOnlyList<RankedFoodItem> Items,
    IReadOnlyList<string> ProvidersSearched,
    IReadOnlyList<ProviderIssue> Issues);

/// <summary>
/// Turns a chat prompt into a ranked top-N list across every connected food
/// provider. Providers are queried in parallel and one failing provider never
/// blocks results from the others.
/// </summary>
public sealed class FoodSearchService(
    IServiceScopeFactory scopeFactory,
    ProviderConnectionService connections,
    IOptions<FoodProvidersOptions> options,
    ILogger<FoodSearchService> logger)
{
    public async Task<FoodAssistResult> AssistAsync(Guid userId, string prompt, bool? vegOnlyOverride, CancellationToken ct)
    {
        var intent = FoodIntentParser.Parse(prompt);
        if (!intent.IsFoodIntent)
        {
            return new FoodAssistResult(false, string.Empty, false, false, [], [], []);
        }

        var vegOnly = vegOnlyOverride ?? intent.VegOnly;
        var statuses = await connections.GetStatusesAsync(userId, ct);
        var targets = statuses
            .Where(s => s is { Enabled: true, Category: "food" })
            .Where(s => intent.PreferredProvider is null || s.Provider == intent.PreferredProvider)
            .ToList();

        var issues = targets
            .Where(t => !t.IsConnected)
            .Select(t => new ProviderIssue(t.Provider, $"Connect {t.DisplayName} to include its results.", true))
            .ToList();

        var connected = targets.Where(t => t.IsConnected).ToList();
        var tasks = connected.Select(t => SearchProviderAsync(userId, t.Provider, intent.Query, vegOnly, ct)).ToList();
        var outcomes = await Task.WhenAll(tasks);

        issues.AddRange(outcomes.Where(o => o.Issue is not null).Select(o => o.Issue!));
        var ranked = Rank(outcomes.SelectMany(o => o.Items), intent.Query)
            .Take(options.Value.TopResults)
            .Select((x, i) => x with { Rank = i + 1 })
            .ToList();

        return new FoodAssistResult(true, intent.Query, vegOnly, intent.AutoPickTop, ranked,
            connected.Select(c => c.Provider).ToList(), issues);
    }

    private async Task<(IReadOnlyList<FoodItem> Items, ProviderIssue? Issue)> SearchProviderAsync(
        Guid userId, string provider, string query, bool vegOnly, CancellationToken ct)
    {
        // Each provider runs in its own scope so DbContext instances are never shared across threads.
        await using var scope = scopeFactory.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var scopedConnections = sp.GetRequiredService<ProviderConnectionService>();
        var addresses = sp.GetRequiredService<AddressService>();
        var foodProvider = sp.GetRequiredService<FoodProviderRegistry>().Get(provider);

        if (foodProvider is null)
        {
            return ([], new ProviderIssue(provider, "This integration is not supported yet.", false));
        }

        try
        {
            var addressId = await addresses.GetDefaultAddressIdAsync(userId, provider, ct);
            if (addressId is null)
            {
                return ([], new ProviderIssue(provider, $"Add a delivery address in your {provider} account first.", false));
            }

            var items = await scopedConnections.WithSessionAsync(userId, provider,
                s => foodProvider.SearchDishesAsync(s, query, addressId, vegOnly, ct), ct);
            return (items, null);
        }
        catch (ProviderNotConnectedException ex)
        {
            return ([], new ProviderIssue(provider, ex.Message, true));
        }
        catch (Exception ex) when (ex is McpException or FoodProviderException or HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "Search failed for {Provider}", provider);
            return ([], new ProviderIssue(provider, $"{provider} search is unavailable right now.", false));
        }
    }

    /// <summary>
    /// Score = rating quality (weighted by how many people rated) + name match +
    /// bestseller boost; out-of-stock items are dropped and items needing
    /// customisation are ranked lower because they can't be auto-ordered.
    /// </summary>
    private static IEnumerable<RankedFoodItem> Rank(IEnumerable<FoodItem> items, string query)
    {
        var words = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        return items
            .Where(i => i.InStock)
            .DistinctBy(i => (i.Provider, i.MenuItemId))
            .Select(i =>
            {
                var rating = Math.Clamp(i.Rating ?? 3.5, 0, 5);
                var confidence = Math.Log10((i.RatingCount ?? 0) + 1);
                var nameMatch = words.Count(w => i.Name.Contains(w, StringComparison.OrdinalIgnoreCase)) / (double)Math.Max(words.Length, 1);
                var score = rating * 2
                            + confidence
                            + nameMatch * 3
                            + (i.IsBestseller ? 1 : 0)
                            - (i.RequiresCustomization ? 1.5 : 0)
                            - (i.Price is null ? 1 : 0);
                return new RankedFoodItem(0, Math.Round(score, 3), i);
            })
            .OrderByDescending(r => r.Score)
            .ThenBy(r => r.Item.Price ?? decimal.MaxValue);
    }
}
