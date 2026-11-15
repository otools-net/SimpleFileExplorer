using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Otools.Net.Sfe.Core.Access;
using Otools.Net.Sfe.Core.Tasks;

namespace Otools.Net.Sfe.Core.Connections.Indexing;

/// <summary>
/// The "Connection index" task of a library: one sync at a time per connection (<see cref="ConnectionIndexer"/>),
/// started when the connection is first opened in the library (and again when its index is older than
/// <see cref="SyncAfter"/>), by "Sync now", or for every connection the library indexes (the task's start, its
/// schedules). Each sync is a run of the history. Rows of a connection the library no longer reaches (removed, no
/// longer offered to it) are dropped (<see cref="PruneAsync"/>). Without the server's services (the Core's own tests)
/// nothing runs.
/// </summary>
public sealed class ConnectionIndexes(
    ConnectionIndexer indexer,
    RemoteIndexTransactions store,
    RemoteIndexCleanup cleanup,
    TaskHistory history,
    TenantBinding tenant,
    TimeProvider clock,
    ILogger<ConnectionIndexes> logger,
    ConnectionIndexSources? sources = null
) : IDisposable
{
    /// <summary>Opening a connection whose index is older than this syncs it.</summary>
    public static readonly TimeSpan SyncAfter = TimeSpan.FromMinutes(10);

    /// <summary>How often opening a connection reads its index's state at most.</summary>
    private static readonly TimeSpan CheckEvery = TimeSpan.FromMinutes(1);

    private readonly ConcurrentDictionary<int, ConnectionIndexRun> _running = new();
    private readonly ConcurrentDictionary<int, DateTimeOffset> _checked = new();
    private readonly CancellationTokenSource _stopping = new();
    private int _all;

    public bool IsAvailable => sources is not null;

    /// <summary>The syncs running now.</summary>
    public IReadOnlyList<ConnectionIndexRun> Running => [.. _running.Values.OrderBy(r => r.Started)];

    /// <summary>
    /// A connection was opened in the library: its first sync starts, or a new one when its index is old. Reads
    /// the state at most once a minute per connection; never fails the listing that opened it.
    /// </summary>
    public async Task OpenedAsync(int connectionId, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        if (!IsAvailable || _running.ContainsKey(connectionId))
            return;
        if (_checked.TryGetValue(connectionId, out var last) && now - last < CheckEvery)
            return;
        _checked[connectionId] = now;
        try
        {
            if (await IsStaleAsync(connectionId, now, cancellationToken))
                await StartAsync(connectionId, TaskTrigger.Automatic, null);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            logger.LogWarning(error, "Connection {ConnectionId}: its index could not be synced", connectionId);
        }
    }

    private async Task<bool> IsStaleAsync(int connectionId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var state = await store.StateAsync(connectionId, cancellationToken);
        return state?.LastSync is not { } synced || now - new DateTimeOffset(synced, TimeSpan.Zero) > SyncAfter;
    }

    /// <summary>Starts a sync of one connection; false when one runs already (or nothing can run).</summary>
    public async Task<bool> StartAsync(int connectionId, TaskTrigger trigger, int? scheduleId)
    {
        if (sources is null || _stopping.IsCancellationRequested)
            return false;
        var sync = new ConnectionIndexRun(connectionId, clock.GetUtcNow());
        if (!_running.TryAdd(connectionId, sync))
            return false;
        var info = await FindAsync(sync);
        if (info is null)
            return false;
        sync.Name = info.Level == ConnectionLevel.Account ? null : info.Name;
        sync.Done = Task.Run(() => RunAsync(info, sync, (trigger, scheduleId)));
        return true;
    }

    /// <summary>Syncs every connection the library indexes, one after the other.</summary>
    public TaskStartResult StartAll(TaskTrigger trigger, int? scheduleId)
    {
        if (sources is null)
            return TaskStartResult.Unavailable;
        if (Interlocked.Exchange(ref _all, 1) == 1)
            return TaskStartResult.AlreadyRunning;
        _ = Task.Run(() => SyncAllAsync(trigger, scheduleId));
        return TaskStartResult.Started;
    }

    /// <summary>Stops the syncs running; false when none was.</summary>
    public bool Cancel()
    {
        var any = false;
        foreach (var sync in _running.Values)
        {
            sync.Stop.Cancel();
            any = true;
        }
        return any;
    }

    public async Task<ConnectionIndexStatus> StatusAsync(int connectionId, CancellationToken cancellationToken)
    {
        var state = await store.StateAsync(connectionId, cancellationToken);
        var running = _running.GetValueOrDefault(connectionId);
        return new ConnectionIndexStatus(
            state is not null || running is not null,
            running is not null,
            state is null || state.Phase == Data.RemoteIndexPhase.Listing,
            state?.Files ?? 0,
            state?.Folders ?? 0,
            state?.LastSync is { } synced ? new DateTimeOffset(synced, TimeSpan.Zero) : null,
            state?.TooLong ?? 0,
            state?.LastError,
            running?.Describe()
        );
    }

    /// <summary>Stops the connection's sync and drops its rows from the library's index (batched).</summary>
    public async Task RemoveAsync(int connectionId, CancellationToken cancellationToken)
    {
        if (_running.TryGetValue(connectionId, out var sync))
        {
            sync.Stop.Cancel();
            await sync.Done;
        }
        await cleanup.ClearAsync(connectionId, cancellationToken);
        _checked.TryRemove(connectionId, out _);
    }

    /// <summary>
    /// Drops the rows of the connections the library no longer reaches: removed, or a server's connection no longer
    /// offered to it.
    /// </summary>
    public async Task PruneAsync(CancellationToken cancellationToken)
    {
        if (sources is null)
            return;
        foreach (var id in await IndexedAsync(cancellationToken))
        {
            var info = await sources.Connections.FindAsync(id, cancellationToken);
            if (info is not null && Reaches(info))
                continue;
            logger.LogInformation("Connection {ConnectionId} is no longer used here: its index is dropped", id);
            await RemoveAsync(id, cancellationToken);
        }
    }

    /// <summary>
    /// Prunes at once, then each time the accounts' database changed (checked every <paramref name="every"/>), for
    /// the lifetime of the library.
    /// </summary>
    public async Task WatchAsync(TimeSpan every, CancellationToken stoppingToken)
    {
        if (sources is null)
            return;
        long? seen = null;
        while (!stoppingToken.IsCancellationRequested)
        {
            var version = sources.Access.Version;
            if (version != seen)
            {
                await TryPruneAsync(stoppingToken);
                seen = version;
            }
            await Task.Delay(every, clock, stoppingToken);
        }
    }

    public void Dispose()
    {
        _stopping.Cancel();
        Cancel();
    }

    private async Task TryPruneAsync(CancellationToken cancellationToken)
    {
        try
        {
            await PruneAsync(cancellationToken);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            logger.LogWarning(error, "The index of connections no longer used could not be dropped");
        }
    }

    /// <summary>The connection of a sync about to start; null (and the sync forgotten) when the library doesn't
    /// reach it.</summary>
    private async Task<ConnectionInfo?> FindAsync(ConnectionIndexRun sync)
    {
        ConnectionInfo? info = null;
        try
        {
            info = await sources!.Connections.FindAsync(sync.ConnectionId, _stopping.Token);
            return info is not null && Reaches(info) ? info : null;
        }
        finally
        {
            if (info is null || !Reaches(info))
                _running.TryRemove(sync.ConnectionId, out _);
        }
    }

    private async Task<List<int>> IndexedAsync(CancellationToken cancellationToken)
    {
        await using var db = await store.ContextAsync(cancellationToken);
        return await db.RemoteIndexes.Select(s => s.ConnectionId).OrderBy(id => id).ToListAsync(cancellationToken);
    }

    private async Task SyncAllAsync(TaskTrigger trigger, int? scheduleId)
    {
        try
        {
            foreach (var id in await IndexedAsync(_stopping.Token))
            {
                if (await StartAsync(id, trigger, scheduleId) && _running.TryGetValue(id, out var sync))
                    await sync.Done;
            }
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            logger.LogWarning(error, "The connections' index could not be synced");
        }
        finally
        {
            Interlocked.Exchange(ref _all, 0);
        }
    }

    private async Task RunAsync(ConnectionInfo info, ConnectionIndexRun sync, (TaskTrigger, int?) started)
    {
        try
        {
            await new ConnectionIndexJob(indexer, store, history, sources!, logger).RunAsync(info, sync, started);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            logger.LogError(error, "Connection {ConnectionId}: the sync failed", info.Id);
        }
        finally
        {
            _running.TryRemove(info.Id, out _);
        }
    }

    /// <summary>The library uses the connection: an account's own (its owner works here), the library's, or the
    /// server's offered to it.</summary>
    private bool Reaches(ConnectionInfo info) =>
        tenant.TenantId is not { } here
        || info.Level switch
        {
            ConnectionLevel.Account => true,
            ConnectionLevel.Library => info.TenantId == here,
            _ => info.AllTenants || info.TenantIds.Contains(here),
        };
}
