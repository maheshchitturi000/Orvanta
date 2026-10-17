using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Orvanta.AuthService.Data;
using Orvanta.AuthService.Notifications;
using Orvanta.AuthService.Security;
using Orvanta.AuthService.Services;
using Orvanta.Shared.Extensions;

var builder = WebApplication.CreateBuilder(args);

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

if (app.Environment.IsDevelopment())
{
    // Development convenience. For production, generate EF Core migrations
    // (dotnet ef migrations add Initial) and apply them in your release pipeline.
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<AuthDbContext>().Database.EnsureCreatedAsync();
}

app.UseForwardedHeaders();
app.UseOrvantaExceptionHandling();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();
