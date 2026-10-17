using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Events;

namespace Orvanta.Shared.Extensions;

public static class LoggingExtensions
{
    /// <summary>
    /// Console + rolling daily log files at
    /// <c>Microservices/logs/{service}-yyyyMMdd.log</c> (kept 14 days).
    /// Levels can still be tuned with the usual "Serilog" config section.
    /// </summary>
    public static WebApplicationBuilder AddOrvantaLogging(this WebApplicationBuilder builder, string serviceName)
    {
        var logDirectory = Path.Combine(FindMicroservicesRoot(builder.Environment.ContentRootPath), "logs");

        builder.Services.AddSerilog((services, config) => config
            .ReadFrom.Configuration(builder.Configuration)
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
            .MinimumLevel.Override("System.Net.Http.HttpClient", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("Service", serviceName)
            .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
            .WriteTo.File(
                Path.Combine(logDirectory, $"{serviceName}-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,
                shared: true,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}"));

        return builder;
    }

    /// <summary>Request summary lines (method, path, status, duration) for every call.</summary>
    public static IApplicationBuilder UseOrvantaRequestLogging(this IApplicationBuilder app) =>
        app.UseSerilogRequestLogging();

    // Walk up from the project folder to "Microservices" so all services share one logs folder.
    private static string FindMicroservicesRoot(string contentRoot)
    {
        for (var dir = new DirectoryInfo(contentRoot); dir is not null; dir = dir.Parent)
        {
            if (dir.Name.Equals("Microservices", StringComparison.OrdinalIgnoreCase))
            {
                return dir.FullName;
            }
        }

        return contentRoot;
    }
}
