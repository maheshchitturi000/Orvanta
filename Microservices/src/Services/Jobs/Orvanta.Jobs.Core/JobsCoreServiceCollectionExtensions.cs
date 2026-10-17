using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orvanta.Jobs.Core.Ai;
using Orvanta.Jobs.Core.Cv;
using Orvanta.Jobs.Core.Data;
using Orvanta.Jobs.Core.Sources;
using Orvanta.Jobs.Core.Workflow;

namespace Orvanta.Jobs.Core;

public static class JobsCoreServiceCollectionExtensions
{
    /// <summary>Registers everything the API and the Azure Functions share.</summary>
    public static IServiceCollection AddJobsCore(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<JobsOptions>(configuration.GetSection(JobsOptions.SectionName));

        var connectionString = configuration.GetConnectionString("JobsDb") ?? configuration["JobsDb"]
            ?? throw new InvalidOperationException("Connection string 'JobsDb' is not configured.");
        services.AddDbContext<JobsDbContext>(o => o.UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure(3)));

        services.AddMemoryCache();
        services.AddHttpClient("job-sources", c =>
        {
            c.Timeout = TimeSpan.FromSeconds(30);
            c.DefaultRequestHeaders.UserAgent.ParseAdd("OrvantaJobs/1.0 (+https://github.com/maheshchitturi000/Orvanta)");
        });

        services.AddSingleton<IJobSource, GreenhouseSource>();
        services.AddSingleton<IJobSource, LeverSource>();
        services.AddSingleton<IJobSource, AshbySource>();
        services.AddSingleton<JobSourceRegistry>();
        services.AddSingleton<IJobSearchSource, JSearchSource>();

        services.AddHttpClient<ILlmClient, OllamaLlmClient>(c => c.Timeout = Timeout.InfiniteTimeSpan);
        services.AddScoped<ProfileExtractor>();

        services.AddSingleton<IJobQueue, AzureStorageJobQueue>();
        services.AddScoped<DiscoveryWorkflow>();
        services.AddScoped<ApplicationPreparer>();

        return services;
    }
}
