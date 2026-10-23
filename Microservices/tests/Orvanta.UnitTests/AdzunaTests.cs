using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Orvanta.Jobs.Core;
using Orvanta.Jobs.Core.Sources;

namespace Orvanta.UnitTests;

public class AdzunaTests
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

    private static AdzunaSource Source(StubHandler handler) =>
        new(new StubFactory(handler), Options.Create(new JobsOptions()), NullLogger<AdzunaSource>.Instance);

    private const string SampleResponse = """
        {
          "count": 2,
          "results": [
            {
              "id": "4867353301",
              "title": "<strong>.NET</strong> Core Developer",
              "description": "We need <strong>C#</strong>, ASP.NET Core and Azure. 3+ years experience.",
              "created": "2026-10-17T06:15:00Z",
              "redirect_url": "https://www.adzuna.in/land/ad/4867353301?se=abc",
              "company": { "display_name": "Contoso India" },
              "location": { "display_name": "Chennai, Tamil Nadu", "area": ["India", "Tamil Nadu", "Chennai"] }
            },
            { "id": "1", "title": "", "redirect_url": "https://www.adzuna.in/land/ad/1" }
          ]
        }
        """;

    [Fact]
    public async Task Searches_the_market_and_city_and_maps_listings()
    {
        var handler = new StubHandler(SampleResponse);

        var jobs = await Source(handler).SearchAsync(new JobSearchQuery(".NET Developer", "Chennai, India", false), "my-id:my-key", default);

        var url = handler.Last!.RequestUri!.AbsoluteUri;
        Assert.StartsWith("https://api.adzuna.com/v1/api/jobs/in/search/1?", url);
        Assert.Contains("app_id=my-id", url);
        Assert.Contains("app_key=my-key", url);
        Assert.Contains("what=.NET%20Developer", url);
        Assert.Contains("where=Chennai", url);

        var job = Assert.Single(jobs);
        Assert.Equal("adzuna", job.Source);
        Assert.Equal(".NET Core Developer", job.Title);
        Assert.Equal("Contoso India", job.Company);
        Assert.Equal("Chennai, Tamil Nadu, India", job.Location);
        Assert.Equal("https://www.adzuna.in/land/ad/4867353301?se=abc", job.ApplyUrl);
        Assert.DoesNotContain("<strong>", job.Description);
        Assert.Equal(new DateTime(2026, 10, 17, 6, 15, 0, DateTimeKind.Utc), job.PostedAtUtc);
    }

    [Fact]
    public async Task Country_only_location_searches_the_whole_market()
    {
        var handler = new StubHandler(SampleResponse);

        await Source(handler).SearchAsync(new JobSearchQuery("Software Engineer", "India", false), "id:key", default);

        Assert.DoesNotContain("where=", handler.Last!.RequestUri!.ToString());
    }

    [Fact]
    public async Task Rejected_credentials_mention_the_api_key()
    {
        var error = await Assert.ThrowsAsync<HttpRequestException>(() =>
            Source(new StubHandler("{}", HttpStatusCode.Unauthorized)).SearchAsync(new JobSearchQuery("x", null, false), "id:bad", default));

        Assert.Contains("API key", error.Message);
    }

    [Theory]
    [InlineData("only-one-part")]
    [InlineData(":key")]
    [InlineData("id:")]
    public async Task Credentials_need_id_and_key(string credential) =>
        await Assert.ThrowsAsync<HttpRequestException>(() =>
            Source(new StubHandler(SampleResponse)).SearchAsync(new JobSearchQuery("x", null, false), credential, default));

    [Fact]
    public async Task Markets_adzuna_does_not_cover_return_nothing()
    {
        var handler = new StubHandler(SampleResponse);

        var jobs = await Source(handler).SearchAsync(new JobSearchQuery("x", "Dubai", false), "id:key", default);

        Assert.Empty(jobs);
        Assert.Null(handler.Last);
    }

    [Theory]
    [InlineData("Chennai, India", "Chennai")]
    [InlineData("Chennai", "Chennai")]
    [InlineData("India", null)]
    [InlineData("Remote", null)]
    [InlineData(null, null)]
    public void City_part_of_a_location(string? location, string? expected) =>
        Assert.Equal(expected, AdzunaSource.City(location));
}
