using System.Globalization;
using System.Text.Json;

namespace Orvanta.FoodService.Providers;

/// <summary>Tolerant readers for provider JSON whose shapes vary between fields and versions.</summary>
internal static class JsonRead
{
    public static string? Str(JsonElement e, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (e.ValueKind == JsonValueKind.Object && e.TryGetProperty(key, out var v))
            {
                switch (v.ValueKind)
                {
                    case JsonValueKind.String when !string.IsNullOrWhiteSpace(v.GetString()):
                        return v.GetString();
                    case JsonValueKind.Number:
                        return v.GetRawText();
                }
            }
        }

        return null;
    }

    public static decimal? Dec(JsonElement e, params string[] keys)
    {
        var raw = Str(e, keys);
        return decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : null;
    }

    public static double? Dbl(JsonElement e, params string[] keys)
    {
        var raw = Str(e, keys);
        return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : null;
    }

    public static int? Int(JsonElement e, params string[] keys)
    {
        var raw = Str(e, keys);
        if (raw is null) return null;

        // Handles "1.2K+", "500+", "1,024 ratings".
        var cleaned = new string(raw.Where(c => char.IsDigit(c) || c is '.' or 'k' or 'K').ToArray());
        var multiplier = cleaned.EndsWith('k') || cleaned.EndsWith('K') ? 1000 : 1;
        cleaned = cleaned.TrimEnd('k', 'K');
        return double.TryParse(cleaned, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? (int)(d * multiplier) : null;
    }

    public static bool? Bool(JsonElement e, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (e.ValueKind == JsonValueKind.Object && e.TryGetProperty(key, out var v))
            {
                if (v.ValueKind is JsonValueKind.True or JsonValueKind.False) return v.GetBoolean();
                if (v.ValueKind == JsonValueKind.Number) return v.GetDouble() != 0;
                if (v.ValueKind == JsonValueKind.String && bool.TryParse(v.GetString(), out var b)) return b;
            }
        }

        return null;
    }

    public static bool HasNonEmptyArray(JsonElement e, string key) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Array && v.GetArrayLength() > 0;

    /// <summary>Depth-first search for the first property with one of the names.</summary>
    public static JsonElement? Find(JsonElement e, params string[] names)
    {
        if (e.ValueKind == JsonValueKind.Object)
        {
            foreach (var name in names)
            {
                if (e.TryGetProperty(name, out var v)) return v;
            }

            foreach (var property in e.EnumerateObject())
            {
                if (Find(property.Value, names) is { } found) return found;
            }
        }
        else if (e.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in e.EnumerateArray())
            {
                if (Find(item, names) is { } found) return found;
            }
        }

        return null;
    }

    /// <summary>All objects anywhere in the tree that contain at least one of <paramref name="markers"/>.</summary>
    public static IEnumerable<JsonElement> ObjectsWith(JsonElement e, params string[] markers)
    {
        if (e.ValueKind == JsonValueKind.Object)
        {
            if (markers.Any(m => e.TryGetProperty(m, out _)))
            {
                yield return e;
                yield break;
            }

            foreach (var property in e.EnumerateObject())
            {
                foreach (var found in ObjectsWith(property.Value, markers)) yield return found;
            }
        }
        else if (e.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in e.EnumerateArray())
            {
                foreach (var found in ObjectsWith(item, markers)) yield return found;
            }
        }
    }

    public static bool ContainsText(JsonElement e, string text) =>
        e.GetRawText().Contains(text, StringComparison.OrdinalIgnoreCase);
}
