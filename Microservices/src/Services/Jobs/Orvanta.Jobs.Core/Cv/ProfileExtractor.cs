using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Orvanta.Jobs.Core.Ai;
using Orvanta.Jobs.Core.Matching;

namespace Orvanta.Jobs.Core.Cv;

public sealed record ExtractedProfile(
    string FullName,
    string Email,
    string Phone,
    string Headline,
    string CurrentTitle,
    string Location,
    decimal TotalYearsExperience,
    IReadOnlyList<string> Skills,
    string Summary,
    string LinkedInUrl,
    bool UsedAi);

/// <summary>
/// Turns CV text into a structured profile: the local LLM when available,
/// deterministic rules otherwise. Skills always pass through the catalog so
/// CVs and job descriptions use the same vocabulary.
/// </summary>
public sealed partial class ProfileExtractor(ILlmClient llm)
{
    private const string SystemPrompt =
        "You extract structured data from CVs. Reply with JSON only, using exactly these keys: " +
        "fullName, email, phone, headline, currentTitle, location, totalYearsExperience (number), skills (array of strings), summary (max 60 words), linkedInUrl. " +
        "Use empty strings when unknown. Do not invent facts.";

    public async Task<ExtractedProfile> ExtractAsync(string cvText, CancellationToken ct)
    {
        var rules = FromRules(cvText);

        var reply = await llm.CompleteAsync(SystemPrompt, Truncate(cvText, 12000), json: true, ct);
        if (reply is null) return rules;

        try
        {
            using var doc = JsonDocument.Parse(reply);
            var r = doc.RootElement;

            var aiSkills = r.TryGetProperty("skills", out var s) && s.ValueKind == JsonValueKind.Array
                ? s.EnumerateArray().Select(x => x.GetString() ?? string.Empty).Where(x => x.Length is > 0 and < 50)
                : [];

            // Rules are reliable for contact details; the model is better at titles and summary.
            return new ExtractedProfile(
                Pick(Str(r, "fullName"), rules.FullName),
                Pick(rules.Email, Str(r, "email")),
                Pick(rules.Phone, Str(r, "phone")),
                Pick(Str(r, "headline"), rules.Headline),
                Pick(Str(r, "currentTitle"), rules.CurrentTitle),
                Pick(Str(r, "location"), rules.Location),
                Num(r, "totalYearsExperience") is { } years and > 0 and < 60 ? years : rules.TotalYearsExperience,
                MergeSkills(rules.Skills, aiSkills),
                Pick(Str(r, "summary"), rules.Summary),
                Pick(rules.LinkedInUrl, Str(r, "linkedInUrl")),
                UsedAi: true);
        }
        catch (JsonException)
        {
            return rules;
        }
    }

    public static ExtractedProfile FromRules(string cvText)
    {
        var lines = cvText.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var firstLine = lines.FirstOrDefault() ?? string.Empty;
        var headline = lines.Skip(1).FirstOrDefault(l => l.Length is > 5 and < 120 && !EmailRegex().IsMatch(l)) ?? string.Empty;

        // "Senior .NET Developer | Cloud & Microservices" → "Senior .NET Developer"
        var titleCandidate = headline.Split('|', '·', '–', ',')[0].Trim();
        var currentTitle = RoleWordRegex().IsMatch(titleCandidate) && titleCandidate.Length <= 80 ? titleCandidate : string.Empty;

        return new ExtractedProfile(
            LooksLikeName(firstLine) ? firstLine : string.Empty,
            EmailRegex().Match(cvText).Value,
            PhoneRegex().Match(cvText).Value.Trim(),
            headline,
            currentTitle,
            string.Empty,
            YearsOfExperience(cvText),
            SkillCatalog.Find(cvText),
            string.Empty,
            LinkedInRegex().Match(cvText).Value,
            UsedAi: false);
    }

    /// <summary>"5+ years of experience", "over 7 years", "3.5 years" → the largest plausible number.</summary>
    public static decimal YearsOfExperience(string text)
    {
        var values = YearsRegex().Matches(text)
            .Select(m => decimal.TryParse(m.Groups[1].Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var v) ? v : 0)
            .Where(v => v is > 0 and < 50)
            .ToList();
        return values.Count > 0 ? values.Max() : 0;
    }

    private static IReadOnlyList<string> MergeSkills(IEnumerable<string> rules, IEnumerable<string> ai) =>
        rules.Concat(ai.Select(SkillCatalog.Normalize))
            .DistinctBy(x => x.ToLowerInvariant())
            .Take(60)
            .ToList();

    private static bool LooksLikeName(string line) =>
        line.Length is > 2 and < 60 && line.Split(' ').Length is >= 1 and <= 5 && !line.Any(char.IsDigit) && !line.Contains('@');

    private static string Str(JsonElement e, string key) =>
        e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()!.Trim() : string.Empty;

    private static decimal? Num(JsonElement e, string key) =>
        e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDecimal() : null;

    private static string Pick(string preferred, string fallback) => string.IsNullOrWhiteSpace(preferred) ? fallback : preferred;

    private static string Truncate(string text, int max) => text.Length <= max ? text : text[..max];

    [GeneratedRegex(@"[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}")]
    private static partial Regex EmailRegex();

    [GeneratedRegex(@"(?:\+?\d{1,3}[\s-]?)?(?:\(?\d{2,5}\)?[\s-]?)?\d{3,5}[\s-]?\d{4,5}")]
    private static partial Regex PhoneRegex();

    [GeneratedRegex(@"(?:https?://)?(?:[a-z]{2,3}\.)?linkedin\.com/in/[A-Za-z0-9_-]+/?", RegexOptions.IgnoreCase)]
    private static partial Regex LinkedInRegex();

    [GeneratedRegex(@"(\d{1,2}(?:\.\d)?)\s*\+?\s*(?:years|yrs)", RegexOptions.IgnoreCase)]
    private static partial Regex YearsRegex();

    [GeneratedRegex(@"\b(developer|engineer|architect|manager|analyst|designer|consultant|scientist|lead|administrator|specialist|tester|programmer)\b", RegexOptions.IgnoreCase)]
    private static partial Regex RoleWordRegex();
}
