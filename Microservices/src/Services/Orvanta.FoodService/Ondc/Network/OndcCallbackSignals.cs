using System.Collections.Concurrent;

namespace Orvanta.FoodService.Ondc.Network;

/// <summary>
/// Wakes up requests waiting for a seller's callback (e.g. a quote request waiting for on_select) as soon as it
/// arrives, instead of polling. Waiters also re-read the database on timeout, so a callback handled by another
/// instance is never missed.
/// </summary>
public sealed class OndcCallbackSignals
{
    private readonly ConcurrentDictionary<string, TaskCompletionSource> _waiters = new();

    public void Signal(string transactionId, string action)
    {
        if (_waiters.TryRemove(Key(transactionId, action), out var waiter))
        {
            waiter.TrySetResult();
        }
    }

    /// <summary>Completes when the callback arrives or after <paramref name="timeout"/>, whichever is first.</summary>
    public async Task WaitAsync(string transactionId, string action, TimeSpan timeout, CancellationToken ct)
    {
        var waiter = _waiters.GetOrAdd(Key(transactionId, action), _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
        try
        {
            await waiter.Task.WaitAsync(timeout, ct);
        }
        catch (TimeoutException)
        {
            _waiters.TryRemove(new KeyValuePair<string, TaskCompletionSource>(Key(transactionId, action), waiter));
        }
    }

    private static string Key(string transactionId, string action) => $"{transactionId}|{action}";
}
