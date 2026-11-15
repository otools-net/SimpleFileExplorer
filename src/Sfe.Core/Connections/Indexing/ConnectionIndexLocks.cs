using System.Collections.Concurrent;

namespace Otools.Net.Sfe.Core.Connections.Indexing;

/// <summary>
/// One writer at a time on a connection's rows in a library's index: a page of a sync, a change made through SFE,
/// a batch of a removal. Held for one transaction, never across a provider call.
/// </summary>
public sealed class ConnectionIndexLocks
{
    private readonly ConcurrentDictionary<int, SemaphoreSlim> _locks = new();

    public async Task<IDisposable> EnterAsync(int connectionId, CancellationToken cancellationToken)
    {
        var gate = _locks.GetOrAdd(connectionId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        return new Held(gate);
    }

    private sealed class Held(SemaphoreSlim gate) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
                gate.Release();
        }
    }
}
