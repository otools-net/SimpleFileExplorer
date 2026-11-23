using Microsoft.Extensions.Logging;

namespace Otools.Net.Sfe.Core.Connections.Indexing;

/// <summary>
/// Changes made to a connection's files outside SFE (at the provider: its website, its app, other programs), followed
/// only while someone has one of its folders open (agents/docs/features/change-feed.md): each listing holds a
/// <see cref="ConnectionWatch"/>; while a connection has one, its changes are read every <see cref="PollEvery"/>
/// through the library's index (<see cref="ConnectionIndexes.FollowAsync"/>: written there, so a listing read from it
/// is right) and <see cref="Changed"/> is raised for the watched folders they touched. Providers that tell no changes
/// (<see cref="ProviderCapabilities.ChangeCursor"/>) are not followed. One per library: its index is its own.
/// </summary>
public sealed class ConnectionWatchers(
    ConnectionIndexes indexes,
    TimeProvider clock,
    ILogger<ConnectionWatchers> logger
) : IDisposable
{
    /// <summary>The most folders of a library's connections watched at once: further ones are not.</summary>
    public const int MaxFolders = 256;

    /// <summary>How often a watched connection's changes are read.</summary>
    public static readonly TimeSpan DefaultPollEvery = TimeSpan.FromSeconds(30);

    private readonly Lock _gate = new();
    private readonly Dictionary<int, WatchedConnection> _connections = [];
    private bool _disposed;

    /// <summary>Shorter in tests.</summary>
    internal TimeSpan PollEvery { get; set; } = DefaultPollEvery;

    /// <summary>
    /// A watched folder of a connection changed outside SFE: (connection id, folder path from its root). Raised by
    /// the connection's reading loop.
    /// </summary>
    public event Action<int, string>? Changed;

    /// <summary>The connections whose changes are read now.</summary>
    public IReadOnlyList<int> Connections
    {
        get
        {
            lock (_gate)
            {
                return [.. _connections.Keys];
            }
        }
    }

    /// <summary>
    /// Follows <paramref name="folder"/> (from the connection's root) until the result is disposed; paths are compared
    /// by <paramref name="comparer"/> (the provider's case rule). Nothing past <see cref="MaxFolders"/>.
    /// </summary>
    public ConnectionWatch Watch(int connectionId, string folder, StringComparer comparer)
    {
        lock (_gate)
        {
            if (_disposed)
                return ConnectionWatch.None;
            if (!_connections.TryGetValue(connectionId, out var watched))
            {
                if (IsFull())
                    return ConnectionWatch.None;
                watched = new WatchedConnection(comparer);
                _connections[connectionId] = watched;
                _ = Task.Run(() => LoopAsync(connectionId, watched.Stop.Token));
            }
            else if (!watched.Folders.ContainsKey(folder) && IsFull())
            {
                return ConnectionWatch.None;
            }
            watched.Folders[folder] = watched.Folders.GetValueOrDefault(folder) + 1;
            return new ConnectionWatch(this, connectionId, folder);
        }
    }

    /// <summary>
    /// Reads the connection's changes now and tells the watched folders they touched (the loop's step).
    /// </summary>
    public async Task ReadChangesAsync(int connectionId, CancellationToken cancellationToken)
    {
        ConnectionFollowResult? result;
        try
        {
            result = await indexes.FollowAsync(connectionId, cancellationToken);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            logger.LogWarning(error, "Connection {ConnectionId}: its changes could not be read", connectionId);
            return;
        }
        if (result is not { Changes: > 0 })
            return;
        foreach (var folder in Touched(connectionId, result))
            Changed?.Invoke(connectionId, folder);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            foreach (var watched in _connections.Values)
                watched.Stop.Cancel();
            _connections.Clear();
        }
    }

    internal void Release(int connectionId, string folder)
    {
        lock (_gate)
        {
            if (!_connections.TryGetValue(connectionId, out var watched))
                return;
            if (watched.Folders.GetValueOrDefault(folder) > 1)
            {
                watched.Folders[folder]--;
                return;
            }
            watched.Folders.Remove(folder);
            if (watched.Folders.Count > 0)
                return;
            _connections.Remove(connectionId);
            watched.Stop.Cancel();
        }
    }

    private bool IsFull()
    {
        if (_connections.Values.Sum(c => c.Folders.Count) < MaxFolders)
            return false;
        logger.LogWarning(
            "{Count} folders of connections are watched for changes made outside SFE: no more are",
            MaxFolders
        );
        return true;
    }

    private async Task LoopAsync(int connectionId, CancellationToken stop)
    {
        try
        {
            while (true)
            {
                await Task.Delay(PollEvery, clock, stop);
                await ReadChangesAsync(connectionId, stop);
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
            // No folder of it is watched any more, or the library closes.
        }
    }

    /// <summary>The watched folders of the connection that <paramref name="result"/> touched.</summary>
    private List<string> Touched(int connectionId, ConnectionFollowResult result)
    {
        lock (_gate)
        {
            if (!_connections.TryGetValue(connectionId, out var watched))
                return [];
            var changed = new HashSet<string>(result.Folders, watched.Folders.Comparer);
            return [.. watched.Folders.Keys.Where(f => result.TooManyFolders || changed.Contains(f))];
        }
    }

    private sealed class WatchedConnection(StringComparer comparer)
    {
        public Dictionary<string, int> Folders { get; } = new(comparer);

        public CancellationTokenSource Stop { get; } = new();
    }
}
