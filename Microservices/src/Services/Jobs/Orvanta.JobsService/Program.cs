using System.Text.Json.Serialization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Orvanta.Jobs.Core;
using Orvanta.Jobs.Core.Data;
using Orvanta.JobsService.Services;
using Orvanta.Shared.Extensions;
using Orvanta.Shared.Responses;

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

// Tables, functions and procedures come from Microservices/Database (Deploy-Database.ps1, also run by
// run-all.ps1). Every service shares the Orvanta database, so no service creates schema on its own.

app.UseForwardedHeaders();
app.UseOrvantaRequestLogging();
app.UseOrvantaExceptionHandling();
app.UseAuthentication();
app.UseOrvantaAuditing();
app.UseAuthorization();
app.MapControllers();
app.MapGet("/health", () => Results.Ok(ResponseCls.Ok("Jobs service is running.")));

app.Run();
