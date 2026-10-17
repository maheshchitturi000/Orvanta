using System.Text.RegularExpressions;

namespace Orvanta.FoodService.Services;

public sealed record FoodIntent(bool IsFoodIntent, string Query, bool VegOnly, string? PreferredProvider, bool AutoPickTop);

/// <summary>
/// Lightweight, deterministic intent detection for chat prompts such as
/// "order 2 chicken biryani from swiggy" or "I'm craving veg pizza".
/// Kept behind an interface-free class so it can later be swapped for an
/// LLM-based extractor without touching the search pipeline.
/// </summary>
public static partial class FoodIntentParser
{
    private static readonly HashSet<string> FoodVerbs =
    [
        "order", "eat", "hungry", "craving", "crave", "deliver", "delivery", "food", "dinner", "lunch", "breakfast",
        "snack", "snacks", "meal", "swiggy", "zomato"
    ];

    private static readonly HashSet<string> Dishes =
    [
        "biryani", "pizza", "burger", "dosa", "idli", "vada", "paneer", "chicken", "mutton", "fish", "prawn", "noodles",
        "fried rice", "momos", "momo", "shawarma", "roll", "rolls", "thali", "sandwich", "pasta", "cake", "ice cream",
        "dessert", "coffee", "tea", "juice", "shake", "milkshake", "salad", "kebab", "tikka", "butter chicken", "naan",
        "roti", "paratha", "chole", "bhature", "pav bhaji", "samosa", "chaat", "pani puri", "haleem", "manchurian",
        "soup", "wrap", "taco", "sushi", "waffle", "donut", "falooda", "lassi", "kulfi", "upma", "poha", "uttapam",
        "pongal", "curd rice", "meals", "egg", "omelette", "fries", "nuggets", "wings", "tandoori", "korma", "dal"
    ];

    private static readonly string[] FillerPhrases =
    [
        "can you please", "could you please", "can you", "could you", "please", "i want to", "i would like to",
        "i'd like to", "i want", "i need", "i would like", "i'd like", "get me", "order me", "order for me",
        "for me", "i am", "i'm", "im", "craving for", "craving", "hungry for", "hungry", "let's", "lets",
        "something", "some", "order", "deliver", "delivery", "food", "dish", "dishes", "the best", "best", "top",
        "good", "nice", "tasty", "near me", "nearby", "right now", "now", "today", "tonight", "for dinner",
        "for lunch", "for breakfast", "and", "the", "a", "an", "to", "eat", "have", "on", "via", "using", "with",
        "pick", "choose", "first", "one", "item", "automatically", "auto", "cod", "cash on delivery", "cash",
        "i", "me", "my", "us", "we", "for", "of", "from", "get", "want", "need", "like", "would", "just", "plate",
        "plates", "portion", "portions", "really", "very", "some more", "more"
    ];

    public static FoodIntent Parse(string prompt)
    {
        var text = prompt.Trim().ToLowerInvariant();
        text = NonWordRegex().Replace(text, " ");
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        var hasDish = Dishes.Any(d => Regex.IsMatch(text, $@"\b{Regex.Escape(d)}\b"));
        var hasVerb = words.Any(FoodVerbs.Contains);

        if (!hasDish && !(hasVerb && (text.Contains("food") || text.Contains("hungry") || text.Contains("eat"))))
        {
            return new FoodIntent(false, string.Empty, false, null, false);
        }

        string? provider = null;
        if (Regex.IsMatch(text, @"\b(from|on|via|using)\s+swiggy\b") || text.EndsWith("swiggy")) provider = "swiggy";
        if (Regex.IsMatch(text, @"\b(from|on|via|using)\s+zomato\b") || text.EndsWith("zomato")) provider = "zomato";

        var vegOnly = Regex.IsMatch(text, @"\b(pure\s+veg|veg|vegetarian)\b") && !Regex.IsMatch(text, @"\bnon\s*veg\b");
        var autoPick = Regex.IsMatch(text, @"\b(top|best|first)\s+(one|item|pick)\b|\bauto(matically)?\b|\bjust order\b");

        var query = " " + text + " ";
        query = Regex.Replace(query, @"\b(from|on|via|using)\s+(swiggy|zomato)\b", " ");
        query = Regex.Replace(query, @"\b(swiggy|zomato)\b", " ");
        query = Regex.Replace(query, @"\b(pure\s+veg|vegetarian|non\s*veg)\b", " ");
        query = Regex.Replace(query, @"\b\d+\b", " ");
        foreach (var phrase in FillerPhrases.OrderByDescending(p => p.Length))
        {
            query = Regex.Replace(query, $@"\s{Regex.Escape(phrase)}(?=\s)", " ");
        }

        query = MultiSpaceRegex().Replace(query, " ").Trim();
        if (query.Length == 0 && hasDish)
        {
            query = Dishes.First(d => Regex.IsMatch(text, $@"\b{Regex.Escape(d)}\b"));
        }

        if (vegOnly && query.Length > 0 && !query.Contains("veg"))
        {
            query = $"veg {query}";
        }

        return new FoodIntent(query.Length > 0, query, vegOnly, provider, autoPick);
    }

    [GeneratedRegex(@"[^\p{L}\p{N}' ]+")]
    private static partial Regex NonWordRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex MultiSpaceRegex();
}
