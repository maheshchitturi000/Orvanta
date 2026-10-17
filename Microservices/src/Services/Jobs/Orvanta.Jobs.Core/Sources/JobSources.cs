using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Orvanta.Jobs.Core.Sources;

/// <summary>A job as read from a source, before matching. Only matched jobs are persisted.</summary>
public sealed record SourcedJob(
    string Source,
    string ExternalId,
    string Company,
    string Title,
    string Location,
    bool IsRemote,
    string Url,
    string ApplyUrl,
    string Description,
    DateTime? PostedAtUtc);

/// <summary>"provider:board", e.g. "greenhouse:stripe".</summary>
public sealed record CompanyBoard(string Provider, string Board)
{
    public static bool TryParse(string value, out CompanyBoard board)
    {
        var parts = value.Trim().Split(':', 2, StringSplitOptions.TrimEntries);
        board = parts.Length == 2 && parts[0].Length > 0 && Regex.IsMatch(parts[1], "^[A-Za-z0-9._-]{1,80}$")
            ? new CompanyBoard(parts[0].ToLowerInvariant(), parts[1])
            : new CompanyBoard(string.Empty, string.Empty);
        return board.Provider.Length > 0;
    }

    public override string ToString() => $"{Provider}:{Board}";
}

/// <summary>
/// A place jobs come from. Add a source (e.g. Workable, an aggregator API) by
/// implementing this and registering it; discovery and matching don't change.
/// </summary>
public interface IJobSource
{
    string Provider { get; }
    Task<IReadOnlyList<SourcedJob>> FetchAsync(string board, CancellationToken ct);
}

public sealed class JobSourceRegistry(IEnumerable<IJobSource> sources)
{
    private readonly Dictionary<string, IJobSource> _sources = sources.ToDictionary(s => s.Provider, StringComparer.OrdinalIgnoreCase);

    public IJobSource? Get(string provider) => _sources.GetValueOrDefault(provider);

    public IReadOnlyCollection<string> Providers => _sources.Keys;
}

internal static partial class Html
{
    public static string ToText(string? html)
    {
        if (string.IsNullOrEmpty(html)) return string.Empty;
        var decoded = WebUtility.HtmlDecode(html);
        decoded = BlockTags().Replace(decoded, "\n");
        decoded = AnyTag().Replace(decoded, " ");
        decoded = WebUtility.HtmlDecode(decoded);
        return Spaces().Replace(decoded, " ").Trim();
    }

    public static string Truncate(string text, int max = 20000) => text.Length <= max ? text : text[..max];

    [GeneratedRegex(@"<\s*(br|/p|/li|/h\d|/div)\s*/?>", RegexOptions.IgnoreCase)]
    private static partial Regex BlockTags();

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex AnyTag();

    [GeneratedRegex(@"[ \t]+")]
    private static partial Regex Spaces();
}

/// <summary>Greenhouse Job Board API: https://developers.greenhouse.io/job-board.html</summary>
public sealed class GreenhouseSource(IHttpClientFactory factory) : IJobSource
{
    public string Provider => "greenhouse";

    public async Task<IReadOnlyList<SourcedJob>> FetchAsync(string board, CancellationToken ct)
    {
        var http = factory.CreateClient("job-sources");
        var root = await http.GetFromJsonAsync<JsonElement>($"https://boards-api.greenhouse.io/v1/boards/{board}/jobs?content=true", ct);

        return root.GetProperty("jobs").EnumerateArray().Select(j =>
        {
            var location = j.TryGetProperty("location", out var l) && l.ValueKind == JsonValueKind.Object ? Str(l, "name") : string.Empty;
            var url = Str(j, "absolute_url");
            return new SourcedJob(
                Provider,
                j.GetProperty("id").GetRawText(),
                Str(j, "company_name") is { Length: > 0 } c ? c : board,
                Str(j, "title"),
                location,
                location.Contains("remote", StringComparison.OrdinalIgnoreCase),
                url,
                url,
                Html.Truncate(Html.ToText(Str(j, "content"))),
                Date(Str(j, "first_published")) ?? Date(Str(j, "updated_at")));
        }).ToList();
    }

    private static string Str(JsonElement e, string key) => e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : string.Empty;

    private static DateTime? Date(string value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d.UtcDateTime : null;
}

/// <summary>Lever Postings API: https://github.com/lever/postings-api</summary>
public sealed class LeverSource(IHttpClientFactory factory) : IJobSource
{
    public string Provider => "lever";

    public async Task<IReadOnlyList<SourcedJob>> FetchAsync(string board, CancellationToken ct)
    {
        var http = factory.CreateClient("job-sources");
        var root = await http.GetFromJsonAsync<JsonElement>($"https://api.lever.co/v0/postings/{board}?mode=json", ct);

        return root.EnumerateArray().Select(j =>
        {
            var categories = j.TryGetProperty("categories", out var c) ? c : default;
            var location = categories.ValueKind == JsonValueKind.Object ? Str(categories, "location") : string.Empty;
            var lists = j.TryGetProperty("lists", out var ls) && ls.ValueKind == JsonValueKind.Array
                ? string.Join("\n", ls.EnumerateArray().Select(x => $"{Str(x, "text")}\n{Html.ToText(Str(x, "content"))}"))
                : string.Empty;
            var description = $"{Str(j, "descriptionPlain")}\n{lists}\n{Str(j, "additionalPlain")}";
            var created = j.TryGetProperty("createdAt", out var ca) && ca.TryGetInt64(out var ms)
                ? DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime
                : (DateTime?)null;

            return new SourcedJob(
                Provider,
                Str(j, "id"),
                CultureInfo.InvariantCulture.TextInfo.ToTitleCase(board),
                Str(j, "text"),
                location,
                Str(j, "workplaceType").Equals("remote", StringComparison.OrdinalIgnoreCase) || location.Contains("remote", StringComparison.OrdinalIgnoreCase),
                Str(j, "hostedUrl"),
                Str(j, "applyUrl") is { Length: > 0 } apply ? apply : Str(j, "hostedUrl"),
                Html.Truncate(description),
                created);
        }).ToList();
    }

    private static string Str(JsonElement e, string key) => e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : string.Empty;
}

/// <summary>Ashby public job board API: https://developers.ashbyhq.com/docs/public-job-posting-api</summary>
public sealed class AshbySource(IHttpClientFactory factory) : IJobSource
{
    public string Provider => "ashby";

    public async Task<IReadOnlyList<SourcedJob>> FetchAsync(string board, CancellationToken ct)
    {
        var http = factory.CreateClient("job-sources");
        var root = await http.GetFromJsonAsync<JsonElement>($"https://api.ashbyhq.com/posting-api/job-board/{board}", ct);

        return root.GetProperty("jobs").EnumerateArray()
            .Where(j => !j.TryGetProperty("isListed", out var listed) || listed.ValueKind != JsonValueKind.False)
            .Select(j =>
            {
                var secondary = j.TryGetProperty("secondaryLocations", out var s) && s.ValueKind == JsonValueKind.Array
                    ? s.EnumerateArray().Select(x => Str(x, "location")).Where(x => x.Length > 0)
                    : [];
                var location = string.Join(" / ", new[] { Str(j, "location") }.Concat(secondary).Where(x => x.Length > 0));

                return new SourcedJob(
                    Provider,
                    Str(j, "id"),
                    CultureInfo.InvariantCulture.TextInfo.ToTitleCase(board),
                    Str(j, "title"),
                    location,
                    j.TryGetProperty("isRemote", out var r) && r.ValueKind == JsonValueKind.True,
                    Str(j, "jobUrl"),
                    Str(j, "applyUrl") is { Length: > 0 } apply ? apply : Str(j, "jobUrl"),
                    Html.Truncate(Str(j, "descriptionPlain")),
                    DateTimeOffset.TryParse(Str(j, "publishedAt"), CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d.UtcDateTime : null);
            }).ToList();
    }

    private static string Str(JsonElement e, string key) => e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : string.Empty;
}
