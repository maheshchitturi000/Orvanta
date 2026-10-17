using Orvanta.Jobs.Core.Cv;
using Orvanta.Jobs.Core.Domain;
using Orvanta.Jobs.Core.Matching;
using Orvanta.Jobs.Core.Sources;

namespace Orvanta.UnitTests;

public class SkillCatalogTests
{
    [Fact]
    public void Finds_skills_with_aliases_and_symbols()
    {
        var skills = SkillCatalog.Find("Built REST APIs in C# on .NET 8 with ASP.NET Core, EF Core, SQL Server and Angular; deployed to Azure with Docker and k8s.");

        Assert.Contains("C#", skills);
        Assert.Contains(".NET", skills);
        Assert.Contains("ASP.NET Core", skills);
        Assert.Contains("Entity Framework", skills);
        Assert.Contains("SQL Server", skills);
        Assert.Contains("Angular", skills);
        Assert.Contains("Azure", skills);
        Assert.Contains("Kubernetes", skills);
        Assert.Contains("REST APIs", skills);
    }

    [Theory]
    [InlineData("We go to market fast and rest on Fridays; express your ideas and spark change.")]
    [InlineData("Java is great but this is about JavaScriptless pages")]
    public void Ignores_everyday_words(string text)
    {
        var skills = SkillCatalog.Find(text);

        Assert.DoesNotContain("Go", skills);
        Assert.DoesNotContain("REST APIs", skills);
        Assert.DoesNotContain("Express", skills);
        Assert.DoesNotContain("Spark", skills);
        Assert.DoesNotContain("JavaScript", skills);
    }

    [Fact]
    public void Specific_skills_imply_general_ones()
    {
        var expanded = SkillCatalog.Expand(["SQL Server", "Angular"]);

        Assert.Contains("SQL", expanded);
        Assert.Contains("TypeScript", expanded);
        Assert.Contains("JavaScript", expanded);
        Assert.DoesNotContain("React", expanded);
    }

    [Fact]
    public void Normalizes_user_typed_skills() =>
        Assert.Equal("Kubernetes", SkillCatalog.Normalize(" k8s "));
}

public class JobMatcherTests
{
    private static readonly CandidateProfile Profile = new()
    {
        CurrentTitle = ".NET Developer",
        TotalYearsExperience = 5,
        Skills = ["C#", ".NET", "ASP.NET Core", "SQL Server", "Angular", "Azure", "Docker", "Microservices"]
    };

    private static readonly JobPreference Preference = new()
    {
        DesiredTitles = [".NET Developer", "Full Stack Developer"],
        Locations = ["Bengaluru", "London"],
        IncludeRemote = true
    };

    private static SourcedJob Job(string title, string description, string location = "London", bool remote = false) =>
        new("greenhouse", "1", "Acme", title, location, remote, "https://x", "https://x", description, DateTime.UtcNow);

    [Fact]
    public void Strong_fit_scores_above_80()
    {
        var job = Job("Senior .NET Engineer",
            "3+ years building microservices in C# / ASP.NET Core on Azure, SQL Server, Docker. Angular is a plus.");

        var result = JobMatcher.Score(Profile, Preference, job);

        Assert.True(result.Score >= 80, $"score was {result.Score}");
        Assert.Contains("C#", result.MatchedSkills);
        Assert.True(result.MissingSkills.Count == 0, "missing: " + string.Join("|", result.MissingSkills));
    }

    [Fact]
    public void Unrelated_role_is_capped_below_threshold_even_with_skill_overlap()
    {
        var job = Job("Account Executive", "Sell to customers using Azure and SQL Server. 3+ years in sales.");

        var result = JobMatcher.Score(Profile, Preference, job);

        Assert.True(result.Score <= 60, $"score was {result.Score}");
    }

    [Fact]
    public void Experience_gap_lowers_the_score()
    {
        const string description = "C#, ASP.NET Core, Azure, SQL Server, microservices.";
        var junior = JobMatcher.Score(Profile, Preference, Job(".NET Developer", description + " 2+ years."));
        var senior = JobMatcher.Score(Profile, Preference, Job(".NET Developer", description + " 10+ years."));

        Assert.True(junior.Score > senior.Score);
    }

    [Theory]
    [InlineData("Software Engineer, Backend (.NET)", 1.0)]
    [InlineData("Full-Stack Developer", 1.0)]
    [InlineData("Data Scientist", 0.0)]
    public void Role_match_handles_synonyms(string jobTitle, double expected) =>
        Assert.Equal(expected, JobMatcher.RoleMatch(jobTitle, [".NET Developer", "Full Stack Developer"]), 2);

    [Theory]
    [InlineData("Technical Program Manager, Developer Velocity")]
    [InlineData("Director of Product Management, Agentic Software Delivery")]
    [InlineData("Solutions Engineer, Ads Solutions - Singapore")]
    [InlineData("Senior Customer Success Engineer, Japan")]
    public void Other_job_functions_do_not_count_as_role_matches(string jobTitle) =>
        Assert.Equal(0, JobMatcher.RoleMatch(jobTitle, ["Software Engineer", ".NET Developer"]));

    [Fact]
    public void Missing_core_skill_from_the_title_keeps_job_below_threshold()
    {
        var job = Job("Senior Backend Engineer (Ruby)",
            "Microservices on Azure with Docker, SQL Server and C# integrations. 3+ years.");
        var preference = new JobPreference { DesiredTitles = ["Backend Engineer"] };

        var result = JobMatcher.Score(Profile, preference, job);

        Assert.True(result.Score <= 70, $"score was {result.Score}");
    }

    [Fact]
    public void Listing_with_few_skills_cannot_reach_full_skill_score()
    {
        var job = Job(".NET Developer", "Join us. Azure experience welcome.");

        var result = JobMatcher.Score(Profile, Preference, job);

        Assert.True(result.Score < 80, $"score was {result.Score}");
    }

    [Fact]
    public void Filters_by_location_remote_and_excluded_companies()
    {
        Assert.True(JobMatcher.PassesFilters(Preference, Job("x", "", "London, UK")));
        Assert.True(JobMatcher.PassesFilters(Preference, Job("x", "", "Anywhere", remote: true)));
        Assert.False(JobMatcher.PassesFilters(Preference, Job("x", "", "New York")));

        var remoteOnly = new JobPreference { RemoteOnly = true };
        Assert.False(JobMatcher.PassesFilters(remoteOnly, Job("x", "", "London")));

        var excluded = new JobPreference { ExcludedCompanies = ["acme"] };
        Assert.False(JobMatcher.PassesFilters(excluded, Job("x", "")));
    }

    [Fact]
    public void Reads_required_years() =>
        Assert.Equal(3, JobMatcher.RequiredYears("We need 3-5 years of experience, ideally 7+ years in total."));
}

public class CvAndBoardTests
{
    [Fact]
    public void Rules_extract_contact_details_years_and_skills()
    {
        const string cv = """
            Priya Sharma
            Senior .NET Developer building cloud-native apps
            priya.sharma@example.com | +91 98765 43210 | linkedin.com/in/priya-sharma
            6+ years of experience with C#, ASP.NET Core, Angular, SQL Server and Azure.
            """;

        var profile = ProfileExtractor.FromRules(cv);

        Assert.Equal("Priya Sharma", profile.FullName);
        Assert.Equal("Senior .NET Developer building cloud-native apps", profile.CurrentTitle);
        Assert.Equal("priya.sharma@example.com", profile.Email);
        Assert.Contains("98765", profile.Phone);
        Assert.Equal(6, profile.TotalYearsExperience);
        Assert.Contains("Angular", profile.Skills);
        Assert.Contains("linkedin.com/in/priya-sharma", profile.LinkedInUrl);
    }

    [Theory]
    [InlineData("greenhouse:stripe", true)]
    [InlineData(" Lever : spotify ", true)]
    [InlineData("stripe", false)]
    [InlineData("greenhouse:../etc", false)]
    public void Parses_company_boards(string value, bool valid) =>
        Assert.Equal(valid, CompanyBoard.TryParse(value, out _));
}

