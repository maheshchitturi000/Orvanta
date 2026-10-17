using System.Globalization;
using Orvanta.Jobs.Core.Domain;

namespace Orvanta.Jobs.Core.Matching;

/// <summary>
/// Target roles to search for: the user's chosen roles, or, when none are set,
/// roles derived from the CV title and skills so search works out of the box.
/// </summary>
public static class RoleSuggester
{
    public static IReadOnlyList<string> EffectiveRoles(CandidateProfile profile, JobPreference preference) =>
        preference.DesiredTitles.Count > 0 ? preference.DesiredTitles : Suggest(profile);

    public static IReadOnlyList<string> Suggest(CandidateProfile profile)
    {
        var skills = SkillCatalog.Expand(profile.Skills);
        var roles = new List<string>();

        if (!string.IsNullOrWhiteSpace(profile.CurrentTitle))
        {
            roles.Add(TitleCase(profile.CurrentTitle.Trim()));
        }

        var dotnet = skills.Contains(".NET") || skills.Contains("C#");
        var frontend = skills.Contains("Angular") || skills.Contains("React") || skills.Contains("Vue");

        if (dotnet) roles.Add(".NET Developer");
        if (dotnet && frontend) roles.Add("Full Stack Developer");
        if (skills.Contains("Java")) roles.Add("Java Developer");
        if (skills.Contains("Python")) roles.Add("Python Developer");
        if (frontend && !dotnet) roles.Add("Frontend Developer");
        roles.Add("Software Engineer");

        return roles.Distinct(StringComparer.OrdinalIgnoreCase).Take(5).ToList();
    }

    private static string TitleCase(string title) =>
        CultureInfo.InvariantCulture.TextInfo.ToTitleCase(title.ToLowerInvariant()).Replace(".Net", ".NET");
}
