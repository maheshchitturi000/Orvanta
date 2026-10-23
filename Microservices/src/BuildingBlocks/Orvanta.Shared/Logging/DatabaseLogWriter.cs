using System.Data;
using System.Runtime.CompilerServices;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Orvanta.Shared.Logging;

/// <summary>
/// Drains <see cref="DatabaseLogQueue"/> in batches into the Orvanta database through
/// dbo.usp_AppLogs_Insert / dbo.usp_AuditLogs_Insert. A failed batch is retried with back-off,
/// then dropped (it is still in the log files). Its own messages are excluded from the database sink.
/// </summary>
public sealed class DatabaseLogWriter(
    DatabaseLogQueue queue,
    string connectionString,
    int batchSize,
    ILogger<DatabaseLogWriter> logger) : BackgroundService
{
    private const int MaxAttempts = 3;
    private static readonly TimeSpan ShutdownDrainTimeout = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var batch = new List<DatabaseLogRecord>(batchSize);
        try
        {
            while (await queue.Reader.WaitToReadAsync(stoppingToken))
            {
                Fill(batch);
                await WriteWithRetryAsync(batch, stoppingToken);
                batch.Clear();
                ReportDropped();
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down: write what is already queued, but don't hold the process hostage.
            using var drain = new CancellationTokenSource(ShutdownDrainTimeout);
            Fill(batch);
            while (batch.Count > 0 && !drain.IsCancellationRequested)
            {
                await WriteWithRetryAsync(batch, drain.Token, attempts: 1);
                batch.Clear();
                Fill(batch);
            }
        }
    }

    private void Fill(List<DatabaseLogRecord> batch)
    {
        while (batch.Count < batchSize && queue.Reader.TryRead(out var record))
        {
            batch.Add(record);
        }
    }

    private async Task WriteWithRetryAsync(List<DatabaseLogRecord> batch, CancellationToken ct, int attempts = MaxAttempts)
    {
        // Rows already written are skipped on retry, so a retry never duplicates them.
        var written = new StrongBox<int>(0);
        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            try
            {
                await WriteAsync(batch, written, ct);
                return;
            }
            catch (Exception ex) when (ex is SqlException or InvalidOperationException or TimeoutException)
            {
                if (attempt == attempts)
                {
                    logger.LogWarning(ex, "Could not write {Count} log entries to the logs database; they remain in the log files",
                        batch.Count - written.Value);
                    return;
                }

                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(2 * attempt * attempt), ct);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }

    private async Task WriteAsync(List<DatabaseLogRecord> batch, StrongBox<int> written, CancellationToken ct)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(ct);

        for (; written.Value < batch.Count; written.Value++)
        {
            var record = batch[written.Value];
            await using var command = record switch
            {
                AppLogEntry log => AppLogCommand(connection, log),
                AuditEntry audit => AuditCommand(connection, audit),
                _ => throw new InvalidOperationException($"Unknown log record {record.GetType().Name}.")
            };
            await command.ExecuteNonQueryAsync(ct);
        }
    }

    private SqlCommand AppLogCommand(SqlConnection connection, AppLogEntry log)
    {
        var command = Procedure(connection, "dbo.usp_AppLogs_Insert");
        var request = log.Request;
        Add(command, "@LoggedAtUtc", SqlDbType.DateTime2, log.TimestampUtc);
        Add(command, "@ServiceName", SqlDbType.NVarChar, queue.ServiceName, 60);
        Add(command, "@Environment", SqlDbType.NVarChar, queue.Environment, 30);
        Add(command, "@MachineName", SqlDbType.NVarChar, queue.MachineName, 100);
        Add(command, "@LogLevel", SqlDbType.NVarChar, log.Level, 12);
        Add(command, "@Category", SqlDbType.NVarChar, log.Category, 256);
        Add(command, "@Message", SqlDbType.NVarChar, log.Message, -1);
        Add(command, "@ExceptionType", SqlDbType.NVarChar, log.ExceptionType, 256);
        Add(command, "@ExceptionMessage", SqlDbType.NVarChar, log.ExceptionMessage, -1);
        Add(command, "@StackTrace", SqlDbType.NVarChar, log.StackTrace, -1);
        AddRequest(command, request);
        Add(command, "@UserId", SqlDbType.UniqueIdentifier, request?.UserId);
        return command;
    }

    private SqlCommand AuditCommand(SqlConnection connection, AuditEntry audit)
    {
        var command = Procedure(connection, "dbo.usp_AuditLogs_Insert");
        var request = audit.Request;
        Add(command, "@OccurredAtUtc", SqlDbType.DateTime2, audit.TimestampUtc);
        Add(command, "@ServiceName", SqlDbType.NVarChar, queue.ServiceName, 60);
        Add(command, "@Action", SqlDbType.NVarChar, audit.Action, 100);
        Add(command, "@Outcome", SqlDbType.NVarChar, audit.Success ? "Success" : "Failure", 10);
        Add(command, "@UserId", SqlDbType.UniqueIdentifier, audit.UserId ?? request?.UserId);
        Add(command, "@UserEmail", SqlDbType.NVarChar, Truncate(audit.UserEmail ?? request?.UserEmail, 254), 254);
        Add(command, "@EntityType", SqlDbType.NVarChar, Truncate(audit.EntityType, 100), 100);
        Add(command, "@EntityId", SqlDbType.NVarChar, Truncate(audit.EntityId, 100), 100);
        Add(command, "@HttpMethod", SqlDbType.NVarChar, request?.Method, 10);
        Add(command, "@RequestPath", SqlDbType.NVarChar, request?.Path, -1);
        Add(command, "@StatusCode", SqlDbType.Int, audit.StatusCode);
        Add(command, "@DurationMs", SqlDbType.Int, audit.DurationMs);
        Add(command, "@TraceId", SqlDbType.NVarChar, request?.TraceId, 64);
        Add(command, "@ClientIp", SqlDbType.NVarChar, request?.ClientIp, 64);
        Add(command, "@UserAgent", SqlDbType.NVarChar, request?.UserAgent, -1);
        Add(command, "@Details", SqlDbType.NVarChar, audit.Details, -1);
        return command;
    }

    private static void AddRequest(SqlCommand command, RequestInfo? request)
    {
        Add(command, "@TraceId", SqlDbType.NVarChar, request?.TraceId, 64);
        Add(command, "@RequestMethod", SqlDbType.NVarChar, request?.Method, 10);
        Add(command, "@RequestPath", SqlDbType.NVarChar, request?.Path, -1);
        Add(command, "@ClientIp", SqlDbType.NVarChar, request?.ClientIp, 64);
        Add(command, "@UserAgent", SqlDbType.NVarChar, request?.UserAgent, -1);
    }

    private static SqlCommand Procedure(SqlConnection connection, string name) =>
        new(name, connection) { CommandType = CommandType.StoredProcedure, CommandTimeout = 15 };

    private static void Add(SqlCommand command, string name, SqlDbType type, object? value, int size = 0)
    {
        var parameter = command.Parameters.Add(name, type);
        if (size != 0)
        {
            parameter.Size = size;
        }
        parameter.Value = value is string text && size > 0 ? Truncate(text, size) : value ?? DBNull.Value;
    }

    private static string? Truncate(string? value, int max) =>
        value is null || value.Length <= max ? value : value[..max];

    private void ReportDropped()
    {
        var dropped = queue.TakeDroppedCount();
        if (dropped > 0)
        {
            logger.LogWarning("{Count} log entries were dropped because the logs database queue was full", dropped);
        }
    }
}
