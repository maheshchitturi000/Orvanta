using System.Text.Json.Serialization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Orvanta.Jobs.Core;
using Orvanta.Jobs.Core.Data;
using Orvanta.JobsService.Services;
using Orvanta.Shared.Extensions;

var builder = WebApplication.CreateBuilder(args);
builder.AddOrvantaLogging("jobs-service");

builder.Services.AddOrvantaControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddOrvantaJwtAuthentication(builder.Configuration);
builder.Services.AddJobsCore(builder.Configuration);

builder.Services.AddScoped<CandidateService>();
builder.Services.AddScoped<MatchService>();
builder.Services.AddScoped<SourceService>();

// Fallback apply worker (queue unreachable / Functions not running); see LocalApplyWorker.
builder.Services.AddSingleton<Orvanta.JobsService.Workers.LocalApplyWorker>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<Orvanta.JobsService.Workers.LocalApplyWorker>());

builder.Services.Configure<ForwardedHeadersOptions>(o =>
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto);

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    // For production, generate EF Core migrations and apply them in the release pipeline.
    using var scope = app.Services.CreateScope();
    var database = scope.ServiceProvider.GetRequiredService<JobsDbContext>().Database;
    await database.EnsureCreatedAsync();
    await database.ExecuteSqlRawAsync(JobsDbContext.DevelopmentSchemaPatches);
    await database.ExecuteSqlRawAsync("ALTER DATABASE CURRENT SET AUTO_CLOSE OFF");
}

app.UseForwardedHeaders();
app.UseOrvantaRequestLogging();
app.UseOrvantaExceptionHandling();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();
