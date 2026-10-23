using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Orvanta.Shared.Responses;

namespace Orvanta.Shared.Middleware;

/// <summary>
/// Converts unhandled exceptions into a <see cref="ResponseCls"/> without leaking stack traces or
/// internal details to the client. The error is logged with the request context; the database log
/// sink stores it in the AppLogs table under the same trace id the user sees as "Reference".
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
            var (status, message) = Classify(ex, context.TraceIdentifier);

            if (status >= StatusCodes.Status500InternalServerError)
            {
                logger.LogError(ex, "Unhandled exception for {Method} {Path} (trace {TraceId})",
                    context.Request.Method, context.Request.Path, context.TraceIdentifier);
            }
            else
            {
                logger.LogWarning(ex, "Request rejected with {StatusCode} for {Method} {Path} (trace {TraceId})",
                    status, context.Request.Method, context.Request.Path, context.TraceIdentifier);
            }

            if (context.Response.HasStarted)
            {
                throw;
            }

            context.Response.Clear();
            context.Response.StatusCode = status;
            await context.Response.WriteAsJsonAsync(ResponseCls.Fail(status, message));
        }
    }

    /// <summary>Client mistakes become 4xx with a safe message; everything else is a 500 with a reference.</summary>
    private static (int Status, string Message) Classify(Exception ex, string traceId) => ex switch
    {
        BadHttpRequestException bad => (bad.StatusCode, "The request could not be read. Please check it and try again."),
        UnauthorizedAccessException => (StatusCodes.Status401Unauthorized, "Your session has expired. Please sign in again."),
        _ => (StatusCodes.Status500InternalServerError, $"Something went wrong. Reference: {traceId}")
    };
}
