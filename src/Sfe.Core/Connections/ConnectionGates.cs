using System.Collections.Concurrent;

namespace Otools.Net.Sfe.Core.Connections;

/// <summary>
/// How much of a provider account one server uses through a connection: at most <see cref="MaxCalls"/> calls at once
/// (whoever makes them: a library's connection is shared), and none while the provider asked to wait
/// (<see cref="RemoteErrorKind.RateLimited"/>): those are answered at once with the time left, so the server never
/// hammers an account the provider is slowing down. One entry per connection used since the start.
/// </summary>
public sealed class ConnectionGates(TimeProvider clock)
{
    /// <summary>Calls to the provider at once per connection; the next ones wait their turn.</summary>
    public const int MaxCalls = 4;

    /// <summary>How long a call waits for its turn before it is answered "busy".</summary>
    public static readonly TimeSpan MaxWait = TimeSpan.FromSeconds(30);

    /// <summary>The wait assumed when the provider asks to wait without saying how long.</summary>
    public static readonly TimeSpan DefaultWait = TimeSpan.FromSeconds(5);

    private readonly ConcurrentDictionary<int, SemaphoreSlim> _calls = new();
    private readonly ConcurrentDictionary<int, DateTimeOffset> _waits = new();
    private readonly ConcurrentDictionary<int, SemaphoreSlim> _writes = new();

    /// <summary>What is left of the provider's wait for this connection; null when calls may go.</summary>
    public TimeSpan? WaitLeft(int connectionId)
    {
        if (!_waits.TryGetValue(connectionId, out var until))
            return null;
        var left = until - clock.GetUtcNow();
        if (left > TimeSpan.Zero)
            return left;
        _waits.TryRemove(new KeyValuePair<int, DateTimeOffset>(connectionId, until));
        return null;
    }

    /// <summary>The provider asked to wait: no call through this connection until then.</summary>
    public void Wait(int connectionId, TimeSpan? retryAfter)
    {
        var until = clock.GetUtcNow() + (retryAfter is { } wait && wait > TimeSpan.Zero ? wait : DefaultWait);
        _waits.AddOrUpdate(connectionId, until, (_, before) => before > until ? before : until);
    }

    /// <summary>A turn to call the provider, held until disposed; null when none came within
    /// <see cref="MaxWait"/>.</summary>
    public async Task<IDisposable?> EnterAsync(int connectionId, CancellationToken cancellationToken)
    {
        var gate = _calls.GetOrAdd(connectionId, _ => new SemaphoreSlim(MaxCalls, MaxCalls));
        return await gate.WaitAsync(MaxWait, cancellationToken) ? new Turn(gate) : null;
    }

    /// <summary>
    /// The connection's one write turn (a commit, a new folder, a delete), held until disposed: providers refuse
    /// writes at once to one account (Dropbox's <c>too_many_write_operations</c>). Taken before a call's turn.
    /// </summary>
    public async Task<IDisposable> EnterWriteAsync(int connectionId, CancellationToken cancellationToken)
    {
        var gate = _writes.GetOrAdd(connectionId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        return new Turn(gate);
    }

    private sealed class Turn(SemaphoreSlim gate) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
                gate.Release();
        }
    }
}
