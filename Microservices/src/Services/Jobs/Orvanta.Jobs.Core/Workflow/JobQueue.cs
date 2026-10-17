using System.Collections.Concurrent;
using System.Text.Json;
using Azure.Storage.Queues;
using Microsoft.Extensions.Options;

namespace Orvanta.Jobs.Core.Workflow;

/// <summary>Producer side of the background workflow. The Azure Functions are the consumers.</summary>
public interface IJobQueue
{
    Task EnqueueDiscoveryAsync(DiscoveryRequest request, CancellationToken ct);
    Task EnqueueApplyAsync(ApplyRequest request, CancellationToken ct);
}

/// <summary>Azure Storage Queues (Azurite locally). Messages are Base64 JSON, the format Functions queue triggers expect.</summary>
public sealed class AzureStorageJobQueue(IOptions<JobsOptions> options) : IJobQueue
{
    private readonly ConcurrentDictionary<string, Lazy<Task<QueueClient>>> _clients = new();
    private readonly string _connectionString = options.Value.QueueConnectionString;

    public Task EnqueueDiscoveryAsync(DiscoveryRequest request, CancellationToken ct) =>
        SendAsync(JobQueues.Discovery, request, ct);

    public Task EnqueueApplyAsync(ApplyRequest request, CancellationToken ct) =>
        SendAsync(JobQueues.Apply, request, ct);

    private async Task SendAsync<T>(string queueName, T message, CancellationToken ct)
    {
        var client = await _clients.GetOrAdd(queueName, name => new Lazy<Task<QueueClient>>(async () =>
        {
            var queue = new QueueClient(_connectionString, name, new QueueClientOptions { MessageEncoding = QueueMessageEncoding.Base64 });
            await queue.CreateIfNotExistsAsync(cancellationToken: CancellationToken.None);
            return queue;
        })).Value;

        await client.SendMessageAsync(JsonSerializer.Serialize(message), ct);
    }
}
