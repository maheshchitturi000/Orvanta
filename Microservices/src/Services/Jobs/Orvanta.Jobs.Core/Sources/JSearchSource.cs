using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Orvanta.Jobs.Core.Sources;

/// <summary>A keyword search (role + place), as opposed to reading one company's board.</summary>
public sealed record JobSearchQuery(string Role, string? Location, bool RemoteOnly);

/// <summary>Search-based source (aggregators). Discovery turns target roles × locations into queries.</summary>
public interface IJobSearchSource
{
    /// <summary>Connection key, e.g. "jsearch".</summary>
    string Name { get; }

    /// <summary>What users see, e.g. "LinkedIn · Naukri · Indeed".</summary>
    string DisplayName { get; }

    Task<IReadOnlyList<SourcedJob>> SearchAsync(JobSearchQuery query, string apiKey, CancellationToken ct);
}

/// <summary>
/// JSearch via RapidAPI (GET /search). Each job's <see cref="SourcedJob.Source"/> is the site that
/// published it ("linkedin", "naukri", "indeed"...), so the UI can say "via LinkedIn".
/// </summary>
public sealed partial class JSearchSource(IHttpClientFactory factory, IOptions<JobsOptions> options, ILogger<JSearchSource> logger) : IJobSearchSource
{
    private readonly JSearchOptions _options = options.Value.JSearch;

    public string Name => "jsearch";

    public string DisplayName => "LinkedIn · Naukri · Indeed";

    public async Task<IReadOnlyList<SourcedJob>> SearchAsync(JobSearchQuery query, string apiKey, CancellationToken ct)
    {
        var country = query.Location is null ? null : CountryCodes.Resolve(query.Location);
        var text = query.RemoteOnly
            ? $"{query.Role} remote"
            : string.IsNullOrWhiteSpace(query.Location) ? query.Role : $"{query.Role} in {query.Location}";

        var parameters = new Dictionary<string, string?>
        {
            ["query"] = text,
            ["page"] = "1",
            ["num_pages"] = Math.Clamp(_options.PagesPerQuery, 1, 5).ToString(CultureInfo.InvariantCulture),
            ["date_posted"] = _options.DatePosted
        };
        if (country is not null) parameters["country"] = country;
        if (query.RemoteOnly) parameters["work_from_home"] = "true";

        var queryString = string.Join("&", parameters.Select(p => $"{p.Key}={Uri.EscapeDataString(p.Value ?? string.Empty)}"));
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{_options.BaseUrl.TrimEnd('/')}/search?{queryString}");
        request.Headers.Add("x-rapidapi-key", apiKey);
        request.Headers.Add("x-rapidapi-host", _options.Host);

        using var response = await factory.CreateClient("job-sources").SendAsync(request, ct);
        if ((int)response.StatusCode is 401 or 403)
            throw new HttpRequestException("JSearch rejected the API key (check Jobs:JSearch:ApiKey and your RapidAPI subscription).");
        if ((int)response.StatusCode == 429)
            throw new HttpRequestException("JSearch monthly quota reached.");
        response.EnsureSuccessStatusCode();

        var root = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        if (!root.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
        {
            logger.LogWarning("JSearch returned no data array for \"{Query}\"", text);
            return [];
        }

        return data.EnumerateArray().Select(Map).Where(j => j is not null).Select(j => j!).ToList();
    }

    private static SourcedJob? Map(JsonElement j)
    {
        var id = Str(j, "job_id");
        var title = Str(j, "job_title");
        if (id.Length == 0 || title.Length == 0) return null;

        var applyUrl = Str(j, "job_apply_link");
        var location = Str(j, "job_location") is { Length: > 0 } l
            ? l
            : string.Join(", ", new[] { Str(j, "job_city"), Str(j, "job_state"), Str(j, "job_country") }.Where(x => x.Length > 0));

        var posted = Str(j, "job_posted_at_datetime_utc") is { Length: > 0 } p
                     && DateTimeOffset.TryParse(p, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var d)
            ? d.UtcDateTime
            : j.TryGetProperty("job_posted_at_timestamp", out var ts) && ts.TryGetInt64(out var seconds)
                ? DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime
                : (DateTime?)null;

        return new SourcedJob(
            Publisher(Str(j, "job_publisher"), applyUrl),
            id.Length > 100 ? id[..100] : id,
            Str(j, "employer_name") is { Length: > 0 } company ? company : "Company",
            title,
            location,
            j.TryGetProperty("job_is_remote", out var remote) && remote.ValueKind == JsonValueKind.True,
            Str(j, "job_google_link") is { Length: > 0 } google ? google : applyUrl,
            applyUrl,
            Html.Truncate(Html.ToText(Str(j, "job_description"))),
            posted);
    }

    /// <summary>"LinkedIn" → "linkedin", "Naukri.com" → "naukri"; falls back to the apply link's host.</summary>
    public static string Publisher(string publisher, string applyUrl)
    {
        var name = publisher.Length > 0 ? publisher : Uri.TryCreate(applyUrl, UriKind.Absolute, out var uri) ? uri.Host : "web";
        name = name.ToLowerInvariant().Replace("www.", string.Empty);
        name = NonAlnum().Replace(name.Split('.')[0], string.Empty);
        return name.Length is > 0 and <= 30 ? name : "web";
    }

    private static string Str(JsonElement e, string key) =>
        e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : string.Empty;

    [GeneratedRegex("[^a-z0-9]")]
    private static partial Regex NonAlnum();
}

/// <summary>Maps user-typed places to ISO 3166-1 alpha-2 codes for the search API.</summary>
public static class CountryCodes
{
    private static readonly Dictionary<string, string> Codes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["india"] = "in", ["united states"] = "us", ["usa"] = "us", ["us"] = "us", ["united kingdom"] = "gb", ["uk"] = "gb",
        ["england"] = "gb", ["canada"] = "ca", ["australia"] = "au", ["germany"] = "de", ["france"] = "fr", ["netherlands"] = "nl",
        ["ireland"] = "ie", ["singapore"] = "sg", ["united arab emirates"] = "ae", ["uae"] = "ae", ["dubai"] = "ae", ["saudi arabia"] = "sa",
        ["qatar"] = "qa", ["japan"] = "jp", ["spain"] = "es", ["italy"] = "it", ["poland"] = "pl", ["sweden"] = "se", ["switzerland"] = "ch",
        ["new zealand"] = "nz", ["malaysia"] = "my", ["portugal"] = "pt", ["belgium"] = "be", ["denmark"] = "dk", ["norway"] = "no",
        ["finland"] = "fi", ["austria"] = "at", ["brazil"] = "br", ["mexico"] = "mx", ["south africa"] = "za",
        // Major cities
        ["bengaluru"] = "in", ["bangalore"] = "in", ["hyderabad"] = "in", ["chennai"] = "in", ["pune"] = "in", ["mumbai"] = "in",
        ["delhi"] = "in", ["new delhi"] = "in", ["gurugram"] = "in", ["gurgaon"] = "in", ["noida"] = "in", ["kolkata"] = "in",
        ["kochi"] = "in", ["ahmedabad"] = "in", ["coimbatore"] = "in", ["london"] = "gb", ["manchester"] = "gb", ["berlin"] = "de",
        ["munich"] = "de", ["amsterdam"] = "nl", ["dublin"] = "ie", ["toronto"] = "ca", ["vancouver"] = "ca", ["sydney"] = "au",
        ["melbourne"] = "au", ["new york"] = "us", ["san francisco"] = "us", ["seattle"] = "us", ["austin"] = "us", ["paris"] = "fr"
    };

    /// <summary>"Bengaluru, India" → "in"; unknown places → null (search text still carries the place).</summary>
    public static string? Resolve(string place)
    {
        foreach (var part in place.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Reverse())
        {
            if (Codes.TryGetValue(part, out var code)) return code;
        }
        return Codes.GetValueOrDefault(place.Trim());
    }
}
