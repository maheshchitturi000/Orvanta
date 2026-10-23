using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using Orvanta.Shared.Logging;

namespace Orvanta.Shared.Auditing;

/// <summary>
/// Audits every change made by a signed-in user (POST/PUT/PATCH/DELETE): action, target, status,
/// duration, user and client. Reads (GET) aren't audited. Must run after UseAuthentication.
/// </summary>
public sealed class AuditMiddleware(RequestDelegate next, DatabaseLogQueue queue)
{
    // Route values that identify the thing being changed, in order of preference.
    private static readonly string[] EntityKeys = ["id", "provider", "source", "conversationId", "matchId", "applicationId", "addressId"];

    public async Task InvokeAsync(HttpContext context)
    {
        if (HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method) || HttpMethods.IsOptions(context.Request.Method)
            || context.GetEndpoint()?.Metadata.GetMetadata<SkipAuditAttribute>() is not null)
        {
            await next(context);
            return;
        }

        var started = Stopwatch.GetTimestamp();
        var failed = false;
        try
        {
            await next(context);
        }
        catch
        {
            failed = true;
            throw;
        }
        finally
        {
            if (context.User.Identity?.IsAuthenticated == true)
            {
                var status = failed ? StatusCodes.Status500InternalServerError : context.Response.StatusCode;
                var (action, entityType) = Describe(context);
                queue.Enqueue(new AuditEntry(
                    DateTime.UtcNow,
                    action,
                    Success: status < 400,
                    UserId: null,
                    UserEmail: null,
                    entityType,
                    EntityId(context),
                    status,
                    (int)Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                    Details: null,
                    RequestInfo.From(context)));
            }
        }
    }

    /// <summary>"Integrations.Disconnect" for MVC actions, otherwise "DELETE api/x/{id}".</summary>
    private static (string Action, string? EntityType) Describe(HttpContext context)
    {
        var endpoint = context.GetEndpoint();
        if (endpoint?.Metadata.GetMetadata<ControllerActionDescriptor>() is { } descriptor)
        {
            return ($"{descriptor.ControllerName}.{descriptor.ActionName}", descriptor.ControllerName);
        }

        var pattern = (endpoint as RouteEndpoint)?.RoutePattern.RawText ?? context.Request.Path.Value;
        return ($"{context.Request.Method} {pattern}", null);
    }

    private static string? EntityId(HttpContext context)
    {
        var values = context.Request.RouteValues;
        foreach (var key in EntityKeys)
        {
            if (values.TryGetValue(key, out var value) && value is not null)
            {
                return value.ToString();
            }
        }
        return null;
    }
}
