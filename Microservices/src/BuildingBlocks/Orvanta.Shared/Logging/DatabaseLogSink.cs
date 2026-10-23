using Microsoft.AspNetCore.Http;
using Serilog.Core;
using Serilog.Events;

namespace Orvanta.Shared.Logging;

/// <summary>
/// Serilog sink that queues Warning+ events for AppLogs, with the current request's context
/// (trace id, method, path, user, IP). Runs on the logging thread, so it only enqueues.
/// </summary>
internal sealed class DatabaseLogSink(DatabaseLogQueue queue, IHttpContextAccessor httpContextAccessor, LogEventLevel minimumLevel)
    : ILogEventSink
{
    private static readonly HashSet<string> ExcludedCategories =
    [
        // Its own failures would loop back into the queue.
        typeof(DatabaseLogWriter).FullName!,
        // "HTTP GET /x responded 500": the exception itself is already stored with the same request details.
        "Serilog.AspNetCore.RequestLoggingMiddleware"
    ];

    public void Emit(LogEvent logEvent)
    {
        if (logEvent.Level < minimumLevel)
        {
            return;
        }

        var category = logEvent.Properties.TryGetValue("SourceContext", out var value) && value is ScalarValue { Value: string text }
            ? text
            : null;

        if (category is not null && ExcludedCategories.Contains(category))
        {
            return;
        }

        var exception = logEvent.Exception;
        queue.Enqueue(new AppLogEntry(
            logEvent.Timestamp.UtcDateTime,
            logEvent.Level.ToString(),
            category,
            logEvent.RenderMessage(),
            exception?.GetType().FullName,
            exception?.Message,
            exception?.ToString(),
            RequestInfo.From(httpContextAccessor.HttpContext)));
    }
}
