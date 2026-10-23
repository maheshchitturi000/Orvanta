using Serilog.Events;

namespace Orvanta.Shared.Logging;

/// <summary>
/// "DatabaseLogging" section. Application logs and audit events go to the Orvanta database
/// (AppLogs, AuditLogs) in addition to the rolling log files.
/// </summary>
public sealed class DatabaseLoggingOptions
{
    public const string SectionName = "DatabaseLogging";

    public bool Enabled { get; set; } = true;

    /// <summary>Falls back to ConnectionStrings:Orvanta when empty.</summary>
    public string? ConnectionString { get; set; }

    /// <summary>Lowest level stored in AppLogs. Information and below stay in the files only.</summary>
    public LogEventLevel MinimumLevel { get; set; } = LogEventLevel.Warning;

    /// <summary>Entries waiting to be written. When full (database down for long), new entries are dropped.</summary>
    public int QueueCapacity { get; set; } = 10_000;

    public int BatchSize { get; set; } = 100;
}
