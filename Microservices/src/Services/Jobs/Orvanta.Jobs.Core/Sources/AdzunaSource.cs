using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Orvanta.Jobs.Core.Sources;

/// <summary>
/// Adzuna's official search API (GET /jobs/{country}/search/1). Credentials are "appId:appKey" from
/// developer.adzuna.com. Listings link to Adzuna's redirect URL, as its terms require, which forwards to the
/// original posting.
/// </summary>
public sealed class AdzunaSource(IHttpClientFactory factory, IOptions<JobsOptions> options, ILogger<AdzunaSource> logger) : IJobSearchSource
{
    /// <summary>Markets Adzuna covers.</summary>
    private static readonly HashSet<string> Countries =
        ["gb", "us", "at", "au", "be", "br", "ca", "ch", "de", "es", "fr", "in", "it", "mx", "nl", "nz", "pl", "sg", "za"];

    private readonly AdzunaOptions _options = options.Value.Adzuna;

    public string Name => "adzuna";

    public string DisplayName => "Adzuna";

    public string KeyHelp =>
        "Adzuna rejected these details. On developer.adzuna.com → Dashboard → API Access Details, copy the Application ID and the Application Key.";

    public async Task<IReadOnlyList<SourcedJob>> SearchAsync(JobSearchQuery query, string apiKey, CancellationToken ct)
    {
        var separator = apiKey.IndexOf(':');
        if (separator <= 0 || separator == apiKey.Length - 1)
            throw new HttpRequestException("Adzuna API key must be \"application id:application key\".");
        var appId = apiKey[..separator].Trim();
        var appKey = apiKey[(separator + 1)..].Trim();

        var country = (query.Location is null ? null : CountryCodes.Resolve(query.Location)) ?? _options.DefaultCountry;
        if (!Countries.Contains(country))
        {
            return []; // Adzuna doesn't cover this market
        }

        var parameters = new Dictionary<string, string>
        {
            ["app_id"] = appId,
            ["app_key"] = appKey,
            ["what"] = query.RemoteOnly ? $"{query.Role} remote" : query.Role,
            ["results_per_page"] = Math.Clamp(_options.ResultsPerPage, 1, 50).ToString(CultureInfo.InvariantCulture),
            ["max_days_old"] = Math.Max(_options.MaxDaysOld, 1).ToString(CultureInfo.InvariantCulture),
            ["sort_by"] = "date",
            ["content-type"] = "application/json"
        };
        if (City(query.Location) is { } city) parameters["where"] = city;

        var queryString = string.Join("&", parameters.Select(p => $"{p.Key}={Uri.EscapeDataString(p.Value)}"));
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{_options.BaseUrl.TrimEnd('/')}/{country}/search/1?{queryString}");

        using var response = await factory.CreateClient("job-sources").SendAsync(request, ct);
        if ((int)response.StatusCode is 401 or 403)
            throw new HttpRequestException("Adzuna rejected the API key (check the application id and key).");
        if ((int)response.StatusCode == 429)
            throw new HttpRequestException("Adzuna request quota reached.");
        response.EnsureSuccessStatusCode();

        var root = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        if (!root.RootElement.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array)
        {
            logger.LogWarning("Adzuna returned no results for \"{Role}\" in {Location}", query.Role, query.Location);
            return [];
        }

        return results.EnumerateArray().Select(Map).Where(j => j is not null).Select(j => j!).ToList();
    }

    /// <summary>The city part of "Chennai, India" or "Chennai"; null for a bare country or no location.</summary>
    public static string? City(string? location)
    {
        if (string.IsNullOrWhiteSpace(location)) return null;
        var parts = location.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Where(p => !CountryCodes.IsCountry(p) && !p.Equals("remote", StringComparison.OrdinalIgnoreCase))
            .ToList();
        return parts.Count > 0 ? string.Join(", ", parts) : null;
    }

    public static SourcedJob? Map(JsonElement j)
    {
        var id = j.TryGetProperty("id", out var idValue) ? idValue.ToString() : string.Empty;
        var title = Html.ToText(Str(j, "title"));
        var url = Str(j, "redirect_url");
        if (id.Length == 0 || title.Length == 0 || url.Length == 0) return null;

        // "Chennai, Tamil Nadu" + area ["India", "Tamil Nadu", "Chennai"] → "Chennai, Tamil Nadu, India".
        var place = j.TryGetProperty("location", out var loc) ? Str(loc, "display_name") : string.Empty;
        var country = loc.ValueKind == JsonValueKind.Object && loc.TryGetProperty("area", out var area)
                      && area.ValueKind == JsonValueKind.Array && area.GetArrayLength() > 0
            ? area[0].GetString() ?? string.Empty
            : string.Empty;
        var location = country.Length == 0 || place.Contains(country, StringComparison.OrdinalIgnoreCase)
            ? place
            : place.Length == 0 ? country : $"{place}, {country}";

        var description = Html.Truncate(Html.ToText(Str(j, "description")));
        var posted = DateTimeOffset.TryParse(Str(j, "created"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var d)
            ? d.UtcDateTime
            : (DateTime?)null;
        var company = j.TryGetProperty("company", out var c) ? Html.ToText(Str(c, "display_name")) : string.Empty;

        return new SourcedJob(
            "adzuna",
            id.Length > 100 ? id[..100] : id,
            company.Length > 0 ? company : "Company",
            title,
            location,
            $"{title} {location}".Contains("remote", StringComparison.OrdinalIgnoreCase)
                || description.Contains("work from home", StringComparison.OrdinalIgnoreCase),
            url,
            url,
            description,
            posted);
    }

    private static string Str(JsonElement e, string key) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : string.Empty;
}
