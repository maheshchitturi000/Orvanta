namespace Orvanta.FoodService.Ondc.Sandbox;

public sealed record SandboxDish(string Id, string Name, decimal Price, bool IsVeg, string Category);

public sealed record SandboxRestaurant(string Id, string Name, string LocationId, string Gps, IReadOnlyList<SandboxDish> Dishes);

/// <summary>Sample restaurants served by the sandbox seller. Not real businesses.</summary>
public static class OndcSandboxCatalog
{
    public static readonly IReadOnlyList<SandboxRestaurant> Restaurants =
    [
        new("sbx-meghana", "Sandbox Biryani House", "sbx-meghana-l1", "12.9352,77.6245",
        [
            new("sbx-b1", "Chicken Dum Biryani", 320m, false, "biryani"),
            new("sbx-b2", "Paneer Biryani", 280m, true, "biryani"),
            new("sbx-b3", "Mutton Biryani", 420m, false, "biryani"),
            new("sbx-b4", "Chicken 65", 260m, false, "starter")
        ]),
        new("sbx-udupi", "Sandbox Udupi Kitchen", "sbx-udupi-l1", "12.9719,77.6412",
        [
            new("sbx-d1", "Masala Dosa", 90m, true, "dosa"),
            new("sbx-d2", "Rava Dosa", 110m, true, "dosa"),
            new("sbx-d3", "Idli Vada", 70m, true, "breakfast"),
            new("sbx-d4", "Veg Meals", 160m, true, "meals")
        ]),
        new("sbx-crust", "Sandbox Crust & Co.", "sbx-crust-l1", "12.9121,77.6446",
        [
            new("sbx-p1", "Margherita Pizza", 249m, true, "pizza"),
            new("sbx-p2", "Chicken Tikka Pizza", 349m, false, "pizza"),
            new("sbx-p3", "Garlic Bread", 129m, true, "sides"),
            new("sbx-p4", "Paneer Wrap", 179m, true, "wrap")
        ])
    ];

    /// <summary>Dishes whose name or category contains any word of the query; every dish when nothing matches a word.</summary>
    public static IEnumerable<(SandboxRestaurant Restaurant, IReadOnlyList<SandboxDish> Dishes)> Search(string query)
    {
        var words = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var restaurant in Restaurants)
        {
            var dishes = restaurant.Dishes
                .Where(d => words.Any(w => d.Name.Contains(w, StringComparison.OrdinalIgnoreCase)
                                           || d.Category.Contains(w, StringComparison.OrdinalIgnoreCase)
                                           || restaurant.Name.Contains(w, StringComparison.OrdinalIgnoreCase)))
                .ToList();
            if (dishes.Count > 0)
            {
                yield return (restaurant, dishes);
            }
        }
    }

    public static (SandboxRestaurant Restaurant, SandboxDish Dish)? Find(string? providerId, string? itemId)
    {
        var restaurant = Restaurants.FirstOrDefault(r => r.Id == providerId);
        var dish = restaurant?.Dishes.FirstOrDefault(d => d.Id == itemId);
        return restaurant is null || dish is null ? null : (restaurant, dish);
    }
}
