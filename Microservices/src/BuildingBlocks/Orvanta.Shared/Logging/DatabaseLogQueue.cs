using System.Threading.Channels;

namespace Orvanta.Shared.Logging;

/// <summary>
/// In-memory hand-off between request threads and <see cref="DatabaseLogWriter"/>. Writing never
/// blocks or throws: when the queue is full (database down for a long time) the entry is dropped and
/// counted. Every entry is also in the rolling log files, so nothing is lost for good.
/// </summary>
public sealed class DatabaseLogQueue
{
    private readonly Channel<DatabaseLogRecord> _channel;
    private long _dropped;

    public DatabaseLogQueue(string serviceName, string environment, bool enabled, int capacity)
    {
        ServiceName = serviceName;
        Environment = environment;
        Enabled = enabled;
        _channel = Channel.CreateBounded<DatabaseLogRecord>(new BoundedChannelOptions(Math.Max(capacity, 100))
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true
        });
    }

    public string ServiceName { get; }
    public string Environment { get; }
    public string MachineName { get; } = System.Environment.MachineName;
    public bool Enabled { get; }

    internal ChannelReader<DatabaseLogRecord> Reader => _channel.Reader;

    public void Enqueue(DatabaseLogRecord record)
    {
        if (Enabled && !_channel.Writer.TryWrite(record))
        {
            Interlocked.Increment(ref _dropped);
        }
    }

    /// <summary>Returns and resets the number of entries dropped since the last call.</summary>
    internal long TakeDroppedCount() => Interlocked.Exchange(ref _dropped, 0);
}
