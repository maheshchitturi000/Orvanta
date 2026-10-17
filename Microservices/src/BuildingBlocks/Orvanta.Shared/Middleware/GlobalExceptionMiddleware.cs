using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Orvanta.Shared.Responses;

namespace Orvanta.Shared.Middleware;

/// <summary>
/// Converts unhandled exceptions into a <see cref="ResponseCls"/> without leaking
/// stack traces or internal details to the client.
/// </summary>
public sealed class GlobalExceptionMiddleware(RequestDelegate next, ILogger<GlobalExceptionMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // Client went away; nothing to report.
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled exception for {Method} {Path} (trace {TraceId})",
                context.Request.Method, context.Request.Path, context.TraceIdentifier);

            if (context.Response.HasStarted)
            {
                throw;
            }

            context.Response.Clear();
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            await context.Response.WriteAsJsonAsync(ResponseCls.Fail(
                StatusCodes.Status500InternalServerError,
                $"Something went wrong. Reference: {context.TraceIdentifier}"));
        }
    }
}
