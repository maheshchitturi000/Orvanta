using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using Orvanta.Shared.Extensions;
using Orvanta.Shared.Responses;

var builder = WebApplication.CreateBuilder(args);
builder.AddOrvantaLogging("gateway");

var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];

builder.Services.AddCors(options => options.AddPolicy("frontend", policy => policy
    .WithOrigins(allowedOrigins)
    .AllowAnyHeader()
    .AllowAnyMethod()
    .AllowCredentials())); // required for the HttpOnly refresh-token cookie

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, ct) =>
        await context.HttpContext.Response.WriteAsJsonAsync(
            ResponseCls.Fail(429, "Too many requests. Please wait a moment and try again."), ct);

    // Brute-force protection for login / register / OTP endpoints.
    options.AddPolicy("auth", http => RateLimitPartition.GetFixedWindowLimiter(
        http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));

    options.AddPolicy("api", http => RateLimitPartition.GetFixedWindowLimiter(
        http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 120, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});

// Behind the Azure Container Apps ingress the client IP arrives in X-Forwarded-For; the rate limits need it.
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.KnownIPNetworks.Clear();
    o.KnownProxies.Clear();
});

builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

var app = builder.Build();

app.UseForwardedHeaders();
app.UseOrvantaRequestLogging();
app.UseOrvantaExceptionHandling();
app.UseCors("frontend");
app.UseRateLimiter();

app.MapGet("/health", () => Results.Ok(ResponseCls.Ok("Gateway is running.")));
app.MapReverseProxy();

// Production image: the Angular app is copied into wwwroot, so the web app and the API share one origin
// (the refresh cookie is SameSite=Strict). Every route renders on the client, so unknown paths get the shell.
// In development wwwroot is empty and the app runs on `ng serve` instead.
app.UseStaticFiles();
app.MapFallbackToFile("index.csr.html");

app.Run();
