using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Orvanta.Shared.Auditing;
using Orvanta.Shared.Logging;
using Serilog;
using Serilog.Events;

namespace Orvanta.Shared.Extensions;

public static class LoggingExtensions
{
    /// <summary>
    /// Console + rolling daily log files at
    /// <c>Microservices/logs/{service}-yyyyMMdd.log</c> (kept 14 days), plus Warning+ entries and
    /// audit events in the Orvanta database (AppLogs, AuditLogs) (<c>ConnectionStrings:Orvanta</c>, see
    /// Microservices/Database/README.md). Levels can still be tuned with the usual "Serilog" config section.
    /// </summary>
    public static WebApplicationBuilder AddOrvantaLogging(this WebApplicationBuilder builder, string serviceName)
    {
        var logDirectory = Path.Combine(FindMicroservicesRoot(builder.Environment.ContentRootPath), "logs");

        var dbOptions = builder.Configuration.GetSection(DatabaseLoggingOptions.SectionName).Get<DatabaseLoggingOptions>() ?? new();
        var connectionString = string.IsNullOrWhiteSpace(dbOptions.ConnectionString)
            ? builder.Configuration.GetConnectionString("Orvanta")
            : dbOptions.ConnectionString;
        var databaseEnabled = dbOptions.Enabled && !string.IsNullOrWhiteSpace(connectionString);

        var queue = new DatabaseLogQueue(serviceName, builder.Environment.EnvironmentName, databaseEnabled, dbOptions.QueueCapacity);
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddSingleton(queue);
        builder.Services.AddSingleton<IAuditLogger, AuditLogger>();
        if (databaseEnabled)
        {
            builder.Services.AddHostedService(sp => new DatabaseLogWriter(
                queue, connectionString!, Math.Max(dbOptions.BatchSize, 1), sp.GetRequiredService<ILogger<DatabaseLogWriter>>()));
        }

        builder.Services.AddSerilog((services, config) =>
        {
            config
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
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}");

            if (databaseEnabled)
            {
                config.WriteTo.Sink(new DatabaseLogSink(queue, services.GetRequiredService<IHttpContextAccessor>(), dbOptions.MinimumLevel));
            }
        });

        return builder;
    }

    /// <summary>Request summary lines (method, path, status, duration) for every call.</summary>
    public static IApplicationBuilder UseOrvantaRequestLogging(this IApplicationBuilder app)
    {
        var queue = app.ApplicationServices.GetRequiredService<DatabaseLogQueue>();
        var log = app.ApplicationServices.GetRequiredService<ILoggerFactory>().CreateLogger("Orvanta.Shared.Logging");
        if (queue.Enabled)
        {
            log.LogInformation("Database logging: errors and audit events are stored in the logs database");
        }
        else
        {
            log.LogWarning("Database logging: DISABLED - set ConnectionStrings:Orvanta to store errors and audit events in the Orvanta database");
        }

        return app.UseSerilogRequestLogging();
    }

    /// <summary>Audit trail of every change by a signed-in user. Call after UseAuthentication.</summary>
    public static IApplicationBuilder UseOrvantaAuditing(this IApplicationBuilder app) =>
        app.UseMiddleware<AuditMiddleware>();

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
