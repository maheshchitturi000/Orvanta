using System.Text.RegularExpressions;

namespace Orvanta.Jobs.Core.Matching;

/// <summary>
/// Canonical skills with aliases, used to read skills out of CVs and job
/// descriptions the same way so they can be compared reliably.
/// </summary>
public static class SkillCatalog
{
    // Canonical name followed by aliases (matched case-insensitively on word boundaries).
    // A leading "!" marks a display-only name that is too ambiguous to match on its own
    // ("go", "rest", "express"...); only its aliases are matched.
    private static readonly string[][] Entries =
    [
        // Languages
        ["C#", "c sharp", "csharp"], ["Java"], ["Python"], ["JavaScript", "ecmascript"], ["TypeScript"],
        ["!Go", "golang", "go lang"], ["Rust"], ["C++", "cpp"], ["Kotlin"], ["Swift"], ["PHP"], ["Ruby"], ["Scala"], ["Dart"],
        ["SQL", "t-sql", "tsql", "pl/sql"], ["Bash", "shell scripting", "powershell"],
        // .NET & backend
        [".NET", "dotnet", ".net core", ".net framework", ".net 8", ".net 9", ".net 10"], ["ASP.NET Core", "asp.net", "asp.net mvc", "web api", "asp.net web api"],
        ["Entity Framework", "ef core", "entity framework core"], ["LINQ"], ["Dapper"], ["SignalR"], ["gRPC"], ["GraphQL"],
        ["!REST APIs", "restful", "rest api", "rest apis", "restful apis", "rest services"], ["Microservices", "microservice", "micro services"],
        ["Node.js", "nodejs", "node js"], ["!Express", "express.js", "expressjs"], ["NestJS"], ["Spring Boot", "spring framework"], ["Django"], ["Flask"], ["FastAPI"],
        ["Laravel"], ["Ruby on Rails", "rails"], ["Blazor"], ["WPF"], ["WCF"], ["MVC"],
        // Frontend
        ["Angular", "angularjs", "angular 2+"], ["React", "react.js", "reactjs"], ["Vue", "vue.js", "vuejs"], ["Next.js", "nextjs"],
        ["Svelte"], ["RxJS"], ["NgRx"], ["Redux"], ["HTML", "html5"], ["CSS", "css3"], ["SASS", "scss"], ["Tailwind", "tailwindcss"],
        ["Bootstrap"], ["jQuery"], ["Webpack"], ["Flutter"], ["React Native"],
        // Data
        ["SQL Server", "mssql", "ms sql", "microsoft sql server"], ["PostgreSQL", "postgres"], ["MySQL"], ["Oracle"], ["MongoDB", "mongo"],
        ["Redis"], ["Elasticsearch", "elastic search"], ["Cassandra"], ["DynamoDB"], ["Cosmos DB", "cosmosdb"], ["Snowflake"], ["BigQuery"],
        ["Kafka", "apache kafka"], ["RabbitMQ"], ["!Spark", "apache spark", "pyspark", "spark sql"], ["Airflow"], ["dbt"], ["Power BI", "powerbi"],
        ["Tableau"], ["Pandas"], ["NumPy"], ["ETL"], ["Data Warehousing", "data warehouse"],
        // Cloud & DevOps
        ["Azure", "microsoft azure"], ["AWS", "amazon web services"], ["GCP", "google cloud"], ["Docker"], ["Kubernetes", "k8s", "aks", "eks", "gke"],
        ["Terraform"], ["Ansible"], ["Jenkins"], ["GitHub Actions"], ["Azure DevOps", "vsts"], ["CI/CD", "ci cd", "continuous integration"],
        ["Linux"], ["Git"], ["Helm"], ["Prometheus"], ["Grafana"], ["Azure Functions"], ["Service Bus", "azure service bus"],
        ["AWS Lambda", "lambda functions"], ["Serverless"], ["Nginx"], ["IIS"],
        // AI/ML
        ["Machine Learning"], ["Deep Learning"], ["PyTorch"], ["TensorFlow"], ["NLP", "natural language processing"], ["LLM", "llms", "large language models"],
        ["Generative AI", "genai", "gen ai"], ["LangChain"], ["Computer Vision"], ["MLOps"],
        // Testing & practices
        ["Unit Testing", "unit tests"], ["xUnit"], ["NUnit"], ["Jest"], ["Jasmine"], ["Selenium"], ["Cypress"], ["Playwright"],
        ["TDD", "test driven development"], ["Agile"], ["Scrum"], ["DDD", "domain driven design"], ["OOP", "object oriented programming"],
        ["Design Patterns"], ["SOLID"], ["System Design"], ["Clean Architecture"], ["CQRS"],
        // Security & misc
        ["OAuth", "oauth2", "oauth 2.0"], ["JWT"], ["OWASP"], ["Identity Server", "identityserver"], ["Jira"], ["Figma"], ["Salesforce"], ["SAP"]
    ];

    private static string CanonicalOf(string[] entry) => entry[0].TrimStart('!');

    private static IEnumerable<string> MatchableNames(string[] entry) =>
        entry[0].StartsWith('!') ? entry.Skip(1) : entry;

    private static readonly IReadOnlyList<(string Canonical, Regex Pattern)> Patterns = Entries
        .Select(e => (CanonicalOf(e), new Regex(
            $@"(?<![\w+#])(?:{string.Join("|", MatchableNames(e).Select(Escape))})(?![\w+#])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled)))
        .ToList();

    private static readonly Dictionary<string, string> CanonicalByAlias = Entries
        .SelectMany(e => e.Select(alias => (alias: alias.TrimStart('!'), canonical: CanonicalOf(e))))
        .GroupBy(x => x.alias, StringComparer.OrdinalIgnoreCase)
        .ToDictionary(g => g.Key, g => g.First().canonical, StringComparer.OrdinalIgnoreCase);

    /// <summary>Specific skills that imply a more general one (knowing SQL Server means knowing SQL).</summary>
    private static readonly Dictionary<string, string[]> Implications = new(StringComparer.OrdinalIgnoreCase)
    {
        ["SQL Server"] = ["SQL"], ["PostgreSQL"] = ["SQL"], ["MySQL"] = ["SQL"], ["Oracle"] = ["SQL"], ["Snowflake"] = ["SQL"], ["BigQuery"] = ["SQL"],
        ["ASP.NET Core"] = [".NET"], ["Entity Framework"] = [".NET"], ["Blazor"] = [".NET"], ["WPF"] = [".NET"], ["C#"] = [".NET"],
        ["TypeScript"] = ["JavaScript"], ["Angular"] = ["TypeScript", "JavaScript"], ["React"] = ["JavaScript"], ["Vue"] = ["JavaScript"],
        ["Next.js"] = ["React", "JavaScript"], ["Node.js"] = ["JavaScript"], ["NgRx"] = ["RxJS"], ["SASS"] = ["CSS"], ["Tailwind"] = ["CSS"],
        ["Azure Functions"] = ["Azure", "Serverless"], ["Service Bus"] = ["Azure"], ["AWS Lambda"] = ["AWS", "Serverless"],
        ["Kubernetes"] = ["Docker"], ["Azure DevOps"] = ["CI/CD"], ["GitHub Actions"] = ["CI/CD"], ["Jenkins"] = ["CI/CD"],
        ["xUnit"] = ["Unit Testing"], ["NUnit"] = ["Unit Testing"], ["Jest"] = ["Unit Testing"], ["Scrum"] = ["Agile"],
        ["PyTorch"] = ["Machine Learning"], ["TensorFlow"] = ["Machine Learning"], ["Deep Learning"] = ["Machine Learning"]
    };

    /// <summary>The skills plus everything they imply, normalized.</summary>
    public static HashSet<string> Expand(IEnumerable<string> skills)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pending = new Stack<string>(skills.Select(Normalize));
        while (pending.TryPop(out var skill))
        {
            if (!result.Add(skill)) continue;
            if (Implications.TryGetValue(skill, out var implied))
            {
                foreach (var s in implied) pending.Push(s);
            }
        }
        return result;
    }

    /// <summary>Canonical skills mentioned in the text.</summary>
    public static IReadOnlyList<string> Find(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];
        return Patterns.Where(p => p.Pattern.IsMatch(text)).Select(p => p.Canonical).ToList();
    }

    /// <summary>Maps a user-typed skill to its canonical name, or returns it trimmed if unknown.</summary>
    public static string Normalize(string skill)
    {
        var trimmed = skill.Trim();
        return CanonicalByAlias.TryGetValue(trimmed, out var canonical) ? canonical : trimmed;
    }

    /// <summary>True if the skill (canonical or custom) appears in the text.</summary>
    public static bool Mentions(string text, string skill)
    {
        var canonical = Normalize(skill);
        var entry = Patterns.FirstOrDefault(p => p.Canonical.Equals(canonical, StringComparison.OrdinalIgnoreCase));
        return entry.Pattern is not null
            ? entry.Pattern.IsMatch(text)
            : Regex.IsMatch(text, $@"(?<![\w+#]){Escape(canonical)}(?![\w+#])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static string Escape(string alias) => Regex.Escape(alias).Replace(@"\ ", @"[\s-]?");
}
