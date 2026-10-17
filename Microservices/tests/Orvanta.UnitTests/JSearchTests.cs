using System.Net;
using System.Text;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Orvanta.Jobs.Core;
using Orvanta.Jobs.Core.Domain;
using Orvanta.Jobs.Core.Sources;
using Orvanta.Jobs.Core.Workflow;

namespace Orvanta.UnitTests;

public class JSearchTests
{
    private sealed class StubHandler(string json, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public HttpRequestMessage? Last { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Last = request;
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
        }
    }

    private sealed class StubFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private static IOptions<JobsOptions> Options(int maxQueries = 3) =>
        Microsoft.Extensions.Options.Options.Create(new JobsOptions { JSearch = new JSearchOptions { ApiKey = "test-key", MaxQueriesPerRun = maxQueries } });

    private const string SampleResponse = """
        {
          "status": "OK",
          "data": [
            {
              "job_id": "abc123==",
              "employer_name": "Contoso",
              "job_publisher": "LinkedIn",
              "job_title": ".NET Developer",
              "job_apply_link": "https://www.linkedin.com/jobs/view/123",
              "job_description": "We need C#, ASP.NET Core and Angular. 2+ years.",
              "job_is_remote": false,
              "job_posted_at_datetime_utc": "2026-10-15T09:00:00.000Z",
              "job_city": "Bengaluru", "job_state": "Karnataka", "job_country": "IN",
              "job_google_link": "https://www.google.com/search?q=jobs"
            },
            { "job_id": "", "job_title": "ignored: no id" }
          ]
        }
        """;

    [Fact]
    public async Task Maps_listings_and_sends_rapidapi_headers()
    {
        var handler = new StubHandler(SampleResponse);
        var source = new JSearchSource(new StubFactory(handler), Options(), NullLogger<JSearchSource>.Instance);

        var jobs = await source.SearchAsync(new JobSearchQuery(".NET Developer", "Bengaluru, India", false), default);

        var job = Assert.Single(jobs);
        Assert.Equal("linkedin", job.Source);
        Assert.Equal("Contoso", job.Company);
        Assert.Equal("Bengaluru, Karnataka, IN", job.Location);
        Assert.Equal("https://www.linkedin.com/jobs/view/123", job.ApplyUrl);
        Assert.Equal(new DateTime(2026, 10, 15, 9, 0, 0, DateTimeKind.Utc), job.PostedAtUtc);

        var request = handler.Last!;
        Assert.Equal("test-key", request.Headers.GetValues("x-rapidapi-key").Single());
        Assert.Contains("country=in", request.RequestUri!.Query);
        Assert.Contains("query=.NET%20Developer%20in%20Bengaluru%2C%20India", request.RequestUri.Query);
    }

    [Fact]
    public async Task Rejected_key_raises_a_clear_error()
    {
        var source = new JSearchSource(new StubFactory(new StubHandler("{}", HttpStatusCode.Forbidden)), Options(), NullLogger<JSearchSource>.Instance);

        var error = await Assert.ThrowsAsync<HttpRequestException>(() => source.SearchAsync(new JobSearchQuery("x", null, false), default));
        Assert.Contains("API key", error.Message);
    }

    [Theory]
    [InlineData("LinkedIn", "", "linkedin")]
    [InlineData("Naukri.com", "", "naukri")]
    [InlineData("", "https://www.indeed.co.in/viewjob?jk=1", "indeed")]
    [InlineData("", "not a url", "web")]
    public void Normalizes_publishers(string publisher, string link, string expected) =>
        Assert.Equal(expected, JSearchSource.Publisher(publisher, link));

    [Theory]
    [InlineData("India", "in")]
    [InlineData("Bengaluru, India", "in")]
    [InlineData("London", "gb")]
    [InlineData("Remote", null)]
    public void Resolves_country_codes(string place, string? expected) =>
        Assert.Equal(expected, CountryCodes.Resolve(place));

    [Fact]
    public void Builds_queries_across_roles_first_within_quota()
    {
        var workflow = new DiscoveryWorkflow(null!, new JobSourceRegistry([]), [], new MemoryCache(new MemoryCacheOptions()), Options(maxQueries: 3), NullLogger<DiscoveryWorkflow>.Instance);
        var preference = new JobPreference { DesiredTitles = [".NET Developer", "Full Stack Developer"], Locations = ["Bengaluru", "Remote"] };

        var queries = workflow.BuildQueries(new CandidateProfile(), preference);

        Assert.Equal(3, queries.Count);
        Assert.Equal((".NET Developer", "Bengaluru"), (queries[0].Role, queries[0].Location));
        Assert.Equal(("Full Stack Developer", "Bengaluru"), (queries[1].Role, queries[1].Location));
        Assert.Equal((".NET Developer", "Remote"), (queries[2].Role, queries[2].Location));
    }
}
