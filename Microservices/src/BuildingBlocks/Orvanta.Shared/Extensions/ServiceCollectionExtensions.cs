using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Orvanta.Shared.Middleware;
using Orvanta.Shared.Responses;
using Orvanta.Shared.Security;

namespace Orvanta.Shared.Extensions;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Validates encrypted (JWE) access tokens issued by the Auth service and
    /// returns <see cref="ResponseCls"/> bodies for 401/403.
    /// </summary>
    public static IServiceCollection AddOrvantaJwtAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        var jwt = configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
                  ?? throw new InvalidOperationException("Jwt configuration section is missing.");

        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwt.Issuer,
                    ValidateAudience = true,
                    ValidAudience = jwt.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = JwtKeys.SigningKey(jwt),
                    TokenDecryptionKey = JwtKeys.EncryptionKey(jwt),
                    ValidateLifetime = true,
                    RequireExpirationTime = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                    NameClaimType = OrvantaClaims.Name,
                    RoleClaimType = OrvantaClaims.Role
                };

                options.Events = new JwtBearerEvents
                {
                    OnChallenge = async context =>
                    {
                        context.HandleResponse();
                        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        await context.Response.WriteAsJsonAsync(ResponseCls.Fail(401, "Your session has expired. Please sign in again."));
                    },
                    OnForbidden = async context =>
                    {
                        context.Response.StatusCode = StatusCodes.Status403Forbidden;
                        await context.Response.WriteAsJsonAsync(ResponseCls.Fail(403, "You do not have permission to perform this action."));
                    }
                };
            });

        services.AddAuthorizationBuilder()
            .AddPolicy("UserOnly", p => p.RequireRole(OrvantaRoles.User, OrvantaRoles.Admin))
            .AddPolicy("AdminOnly", p => p.RequireRole(OrvantaRoles.Admin));

        return services;
    }

    /// <summary>
    /// Controllers whose model-validation failures are returned as
    /// <see cref="ResponseCls"/> with a field → errors dictionary in StatusObject.
    /// </summary>
    public static IMvcBuilder AddOrvantaControllers(this IServiceCollection services) =>
        services.AddControllers().ConfigureApiBehaviorOptions(options =>
        {
            options.InvalidModelStateResponseFactory = context =>
            {
                var errors = context.ModelState
                    .Where(kv => kv.Value?.Errors.Count > 0)
                    .ToDictionary(
                        kv => string.IsNullOrEmpty(kv.Key) ? "request" : char.ToLowerInvariant(kv.Key[0]) + kv.Key[1..],
                        kv => kv.Value!.Errors.Select(e => string.IsNullOrEmpty(e.ErrorMessage) ? "Invalid value." : e.ErrorMessage).ToArray());

                return new BadRequestObjectResult(ResponseCls.Fail(400, "Please correct the highlighted fields.", errors));
            };
        });

    public static IApplicationBuilder UseOrvantaExceptionHandling(this IApplicationBuilder app) =>
        app.UseMiddleware<GlobalExceptionMiddleware>();
}
