using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Orvanta.Jobs.Core;

var builder = FunctionsApplication.CreateBuilder(args);

// Local development: read secrets (e.g. Jobs:JSearch:ApiKey) from the same user-secrets store as
// Orvanta.JobsService. In Azure, use Application Settings / Key Vault references instead.
builder.Configuration.AddUserSecrets(typeof(Program).Assembly, optional: true);

// Same domain services as the API: one source of truth for matching and preparing applications.
builder.Services.AddJobsCore(builder.Configuration);

builder.Build().Run();
