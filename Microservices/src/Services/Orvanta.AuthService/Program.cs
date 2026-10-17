using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Orvanta.AuthService.Data;
using Orvanta.AuthService.Notifications;
using Orvanta.AuthService.Security;
using Orvanta.AuthService.Services;
using Orvanta.Shared.Extensions;

var builder = WebApplication.CreateBuilder(args);
builder.AddOrvantaLogging("auth-service");

builder.Services.AddOrvantaControllers();
builder.Services.AddOrvantaJwtAuthentication(builder.Configuration);

builder.Services.AddDbContext<AuthDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("AuthDb")));

builder.Services.Configure<AuthSecurityOptions>(builder.Configuration.GetSection(AuthSecurityOptions.SectionName));
builder.Services.Configure<SmtpOptions>(builder.Configuration.GetSection(SmtpOptions.SectionName));

var security = builder.Configuration.GetSection(AuthSecurityOptions.SectionName).Get<AuthSecurityOptions>() ?? new();
if (string.IsNullOrWhiteSpace(security.PasswordPepper) || string.IsNullOrWhiteSpace(security.OtpHmacKey))
{
    throw new InvalidOperationException("AuthSecurity:PasswordPepper and AuthSecurity:OtpHmacKey must be configured.");
}

builder.Services.AddSingleton<IPasswordHasher>(new PasswordHasher(security.PasswordPepper));
builder.Services.AddSingleton<ITokenService, TokenService>();
builder.Services.AddScoped<IOtpService, OtpService>();
builder.Services.AddScoped<IAuthService, AuthService>();

var smtp = builder.Configuration.GetSection(SmtpOptions.SectionName).Get<SmtpOptions>() ?? new();
if (smtp.IsConfigured)
{
    builder.Services.AddSingleton<IEmailSender, SmtpEmailSender>();
}
else
{
    builder.Services.AddSingleton<IEmailSender, DevelopmentEmailSender>();
}

builder.Services.Configure<ForwardedHeadersOptions>(o =>
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto);

var app = builder.Build();

// Say loudly how verification emails will be delivered, so "I got no email" is easy to diagnose.
var startupLog = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Orvanta.AuthService.Startup");
if (smtp.IsConfigured)
{
    startupLog.LogInformation("Email delivery: SMTP {Host}:{Port} (SSL {Ssl}) as {From} using account {User}",
        smtp.Host, smtp.Port, smtp.EnableSsl, smtp.FromEmail, string.IsNullOrEmpty(smtp.UserName) ? "(none)" : smtp.UserName);
}
else
{
    startupLog.LogWarning("Email delivery: DISABLED - Smtp:Host is not set. Verification codes are written to this log " +
                          "instead of being emailed. Configure Smtp:* in user-secrets to send real emails.");
}

if (app.Environment.IsDevelopment())
{
    // Development convenience. For production, generate EF Core migrations
    // (dotnet ef migrations add Initial) and apply them in your release pipeline.
    using var scope = app.Services.CreateScope();
    var database = scope.ServiceProvider.GetRequiredService<AuthDbContext>().Database;
    await database.EnsureCreatedAsync();
    // SQL Express creates databases with AUTO_CLOSE ON, which adds multi-second delays after idle periods.
    await database.ExecuteSqlRawAsync("ALTER DATABASE CURRENT SET AUTO_CLOSE OFF");
}

app.UseForwardedHeaders();
app.UseOrvantaRequestLogging();
app.UseOrvantaExceptionHandling();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();
