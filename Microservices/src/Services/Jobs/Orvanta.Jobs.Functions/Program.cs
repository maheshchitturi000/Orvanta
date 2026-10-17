using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.Hosting;
using Orvanta.Jobs.Core;

var builder = FunctionsApplication.CreateBuilder(args);

// Same domain services as the API: one source of truth for matching and preparing applications.
builder.Services.AddJobsCore(builder.Configuration);

builder.Build().Run();
