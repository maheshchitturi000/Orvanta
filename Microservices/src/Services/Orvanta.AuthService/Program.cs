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
    options.UseSqlServer(builder.Configuration.GetConnectionString("Orvanta")));

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
