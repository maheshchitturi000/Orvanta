using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Orvanta.FoodService.Data;
using Orvanta.FoodService.Integrations;
using Orvanta.FoodService.Integrations.OAuth;
using Orvanta.FoodService.Ondc;
using Orvanta.FoodService.Ondc.Data;
using Orvanta.FoodService.Ondc.Network;
using Orvanta.FoodService.Ondc.Protocol;
using Orvanta.FoodService.Ondc.Sandbox;
using Orvanta.FoodService.Options;
using Orvanta.FoodService.Providers;
using Orvanta.FoodService.Services;
using Orvanta.Shared.Extensions;
using Orvanta.Shared.Security;

var builder = WebApplication.CreateBuilder(args);
builder.AddOrvantaLogging("food-service");

builder.Services.AddOrvantaControllers();
builder.Services.AddOrvantaJwtAuthentication(builder.Configuration);
builder.Services.AddMemoryCache();

builder.Services.AddDbContext<FoodDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("Orvanta")));

builder.Services.Configure<FoodProvidersOptions>(builder.Configuration.GetSection(FoodProvidersOptions.SectionName));

var dataProtectionKey = builder.Configuration["DataProtection:Key"];
if (string.IsNullOrWhiteSpace(dataProtectionKey))
{
    throw new InvalidOperationException("DataProtection:Key must be configured (base64, 32 bytes).");
}

builder.Services.AddSingleton(new AesGcmProtector(dataProtectionKey));

builder.Services.AddHttpClient("mcp", c => c.Timeout = TimeSpan.FromSeconds(30));
builder.Services.AddHttpClient("oauth", c => c.Timeout = TimeSpan.FromSeconds(15));

// Integration infrastructure
builder.Services.AddScoped<McpOAuthClient>();
builder.Services.AddScoped<ProviderConnectionService>();

// Food providers: add a new platform by implementing IFoodProvider and registering it here.
builder.Services.AddSingleton<IFoodProvider, SwiggyFoodProvider>();
builder.Services.AddSingleton<IFoodProvider, ZomatoFoodProvider>();
builder.Services.AddSingleton<FoodProviderRegistry>();

// Application services
builder.Services.AddScoped<AddressService>();
builder.Services.AddScoped<FoodSearchService>();
builder.Services.AddScoped<FoodOrderService>();

// ONDC buyer app (Ondc/): signed requests out, signed callbacks in, state in the Ondc* tables.
builder.Services.Configure<OndcOptions>(builder.Configuration.GetSection(OndcOptions.SectionName));
builder.Services.AddHttpClient("ondc", c => c.Timeout = TimeSpan.FromSeconds(15));
builder.Services.AddSingleton<OndcIdentity>();
builder.Services.AddSingleton<OndcMessages>();
builder.Services.AddSingleton<IOndcRepository, OndcRepository>();
builder.Services.AddSingleton<OndcRegistry>();
builder.Services.AddSingleton<OndcClient>();
builder.Services.AddSingleton<OndcCallbackSignals>();
builder.Services.AddSingleton<OndcCallbackProcessor>();
builder.Services.AddSingleton<OndcSandboxNetwork>();
builder.Services.AddScoped<OndcBuyerService>();

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

app.Run();
