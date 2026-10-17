using System.Text.RegularExpressions;
using Orvanta.Jobs.Core.Domain;
using Orvanta.Jobs.Core.Sources;

namespace Orvanta.Jobs.Core.Matching;

public sealed record MatchResult(
    int Score,
    IReadOnlyList<string> MatchedSkills,
    IReadOnlyList<string> MissingSkills,
    IReadOnlyList<string> Reasons)
{
    /// <summary>True when a guard (role mismatch, missing core skill, experience gap) limited the score.</summary>
    public bool Capped { get; init; }
}

/// <summary>
/// Deterministic, explainable CV ↔ job scoring (0-100):
///   55% skills  — share of the job's skills found in the CV
///   30% role    — how well the job title matches the roles the user wants
///   15% seniority — required years of experience vs. the candidate's
/// A job with no role overlap is capped below the threshold, so skill overlap alone
/// (e.g. a "Sales Engineer" listing SQL) never triggers an application.
/// </summary>
public static partial class JobMatcher
{
    private const int SkillWeight = 55;
    private const int RoleWeight = 30;
    private const int ExperienceWeight = 15;
    private const int NoRoleCap = 60;
    private const int MissingCoreSkillCap = 70;
    private const int ExperienceGapCap = 70;
    private const decimal MaxToleratedExperienceGap = 2;
    private const int MinSkillsConsidered = 4;
    private const int MaxSkillsConsidered = 10;

    /// <summary>
    /// Words that mark a different job function. A title containing one only matches
    /// when one of the user's target roles contains it too ("Solutions Engineer" ≠ "Software Engineer").
    /// </summary>
    private static readonly string[] FunctionWords =
    [
        "manager", "director", "head", "vp", "president", "chief", "program", "product", "project", "sales", "account",
        "success", "solutions", "solution", "support", "partner", "marketing", "recruiter", "recruiting", "designer",
        "analyst", "consultant", "architect", "scientist", "researcher", "intern", "evangelist", "advocate", "writer",
        "operations", "counsel", "finance", "security", "deployed"
    ];

    private static readonly Dictionary<string, string> TitleSynonyms = new(StringComparer.OrdinalIgnoreCase)
    {
        ["developer"] = "engineer", ["dev"] = "engineer", ["programmer"] = "engineer", ["swe"] = "software engineer",
        ["sde"] = "software engineer", ["fullstack"] = "full stack", ["full-stack"] = "full stack",
        ["frontend"] = "front end", ["front-end"] = "front end", ["backend"] = "back end", ["back-end"] = "back end",
        ["dotnet"] = ".net", ["sr"] = "senior", ["sr."] = "senior", ["jr"] = "junior", ["jr."] = "junior"
    };

    private static readonly HashSet<string> TitleNoise = new(StringComparer.OrdinalIgnoreCase)
    {
        "senior", "junior", "lead", "staff", "principal", "i", "ii", "iii", "iv", "the", "a", "an", "of", "and", "&", "-", "/", "(", ")", "remote", "hybrid"
    };

    public static MatchResult Score(CandidateProfile profile, JobPreference preference, SourcedJob job)
    {
        var reasons = new List<string>();
        var text = $"{job.Title}\n{job.Description}";

        // Skills: catalog skills in the job, plus any custom CV skill the job mentions.
        var cvSkills = SkillCatalog.Expand(profile.Skills);
        var jobSkills = SkillCatalog.Find(text)
            .Concat(profile.Skills.Select(SkillCatalog.Normalize).Where(s => SkillCatalog.Mentions(text, s)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var matched = jobSkills.Where(cvSkills.Contains).ToList();
        var missing = jobSkills.Where(s => !cvSkills.Contains(s)).Take(8).ToList();

        double skillRatio;
        if (jobSkills.Count == 0)
        {
            skillRatio = 0.5;
            reasons.Add("The listing names no specific skills.");
        }
        else
        {
            // A listing naming only 1-2 skills is weak evidence; count at least MinSkillsConsidered.
            var denominator = Math.Clamp(jobSkills.Count, MinSkillsConsidered, MaxSkillsConsidered);
            skillRatio = Math.Min(matched.Count, denominator) / (double)denominator;
            reasons.Add($"You have {matched.Count} of {jobSkills.Count} skills mentioned.");
        }

        // Skills named in the title are the core of the job ("Backend Engineer (Ruby)").
        var missingCore = SkillCatalog.Find(job.Title).Where(s => !cvSkills.Contains(s)).ToList();
        if (missingCore.Count > 0)
        {
            reasons.Add($"Core skill not on your CV: {string.Join(", ", missingCore)}.");
        }

        // Role
        var roleRatio = RoleMatch(job.Title, RoleSuggester.EffectiveRoles(profile, preference));
        reasons.Add(roleRatio >= 0.99 ? "Job title matches a role you want." : roleRatio > 0 ? "Job title partly matches a role you want." : "Job title doesn't match your target roles.");

        // Experience
        var required = RequiredYears(job.Description);
        double experienceRatio;
        if (required is null)
        {
            experienceRatio = IsSenior(job.Title) && profile.TotalYearsExperience < 4 ? 0.5 : 1;
        }
        else
        {
            var gap = required.Value - profile.TotalYearsExperience;
            experienceRatio = gap <= 0 ? 1 : gap <= 1 ? 0.6 : 0.2;
            reasons.Add(gap <= 0
                ? $"Your {profile.TotalYearsExperience:0.#} years meet the {required:0}+ years asked."
                : $"Asks for {required:0}+ years; you have {profile.TotalYearsExperience:0.#}.");
        }

        var raw = (int)Math.Round(skillRatio * SkillWeight + roleRatio * RoleWeight + experienceRatio * ExperienceWeight);
        var score = raw;
        if (roleRatio == 0)
        {
            score = Math.Min(score, NoRoleCap);
        }

        if (missingCore.Count > 0)
        {
            score = Math.Min(score, MissingCoreSkillCap);
        }

        // Applying to a role asking for far more experience wastes the application.
        if (required is not null && required.Value - profile.TotalYearsExperience > MaxToleratedExperienceGap)
        {
            score = Math.Min(score, ExperienceGapCap);
        }

        return new MatchResult(Math.Clamp(score, 0, 100), matched, missing, reasons) { Capped = score < raw || roleRatio == 0 };
    }

    /// <summary>Location / remote / company filters applied before scoring.</summary>
    public static bool PassesFilters(JobPreference preference, SourcedJob job)
    {
        if (preference.ExcludedCompanies.Any(c => job.Company.Contains(c, StringComparison.OrdinalIgnoreCase)))
            return false;

        if (preference.RemoteOnly)
            return job.IsRemote;

        if (preference.Locations.Count == 0)
            return true;

        if (job.IsRemote && preference.IncludeRemote)
            return true;

        return preference.Locations.Any(l => l.Length > 1 && job.Location.Contains(l.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// How well the job title fits the target roles: 1 (all words of a target role), 0.5 (half or more), else 0.
    /// A title from a different job function (manager, sales, solutions...) scores 0 unless a target role shares that word.
    /// </summary>
    public static double RoleMatch(string jobTitle, IEnumerable<string> desiredTitles)
    {
        var jobTokens = Tokens(jobTitle);
        if (jobTokens.Count == 0) return 0;

        var desired = desiredTitles.Select(Tokens).Where(t => t.Count > 0).ToList();
        if (desired.Count == 0) return 0;

        var desiredWords = desired.SelectMany(t => t).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (FunctionWords.Any(w => jobTokens.Contains(w) && !desiredWords.Contains(w)))
        {
            return 0;
        }

        var best = desired.Max(t => t.Count(jobTokens.Contains) / (double)t.Count);
        return best >= 0.99 ? 1 : best >= 0.5 ? 0.5 : 0;
    }

    public static decimal? RequiredYears(string description)
    {
        var values = RequiredYearsRegex().Matches(description)
            .Select(m => decimal.TryParse(m.Groups[1].Value, out var v) ? v : 0)
            .Where(v => v is > 0 and <= 20)
            .ToList();
        return values.Count > 0 ? values.Min() : null;
    }

    private static bool IsSenior(string title) => SeniorRegex().IsMatch(title);

    private static HashSet<string> Tokens(string title)
    {
        var normalized = title.ToLowerInvariant();
        foreach (var (from, to) in TitleSynonyms)
        {
            normalized = Regex.Replace(normalized, $@"(?<![\w.]){Regex.Escape(from)}(?![\w])", to);
        }

        return TokenRegex().Matches(normalized)
            .Select(m => m.Value)
            .Where(t => !TitleNoise.Contains(t))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    [GeneratedRegex(@"(\d{1,2})\s*\+?\s*(?:-\s*\d{1,2}\s*)?(?:years|yrs)", RegexOptions.IgnoreCase)]
    private static partial Regex RequiredYearsRegex();

    [GeneratedRegex(@"\b(senior|sr\.?|lead|staff|principal)\b", RegexOptions.IgnoreCase)]
    private static partial Regex SeniorRegex();

    [GeneratedRegex(@"[a-z0-9.#+]+")]
    private static partial Regex TokenRegex();
}
