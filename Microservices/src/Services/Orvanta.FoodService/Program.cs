using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Orvanta.FoodService.Data;
using Orvanta.FoodService.Integrations;
using Orvanta.FoodService.Integrations.OAuth;
using Orvanta.FoodService.Options;
using Orvanta.FoodService.Providers;
using Orvanta.FoodService.Services;
using Orvanta.Shared.Extensions;
using Orvanta.Shared.Security;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOrvantaControllers();
builder.Services.AddOrvantaJwtAuthentication(builder.Configuration);
builder.Services.AddMemoryCache();

builder.Services.AddDbContext<FoodDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("FoodDb")));

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

builder.Services.Configure<ForwardedHeadersOptions>(o =>
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto);

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<FoodDbContext>().Database.EnsureCreatedAsync();
}

app.UseForwardedHeaders();
app.UseOrvantaExceptionHandling();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();
