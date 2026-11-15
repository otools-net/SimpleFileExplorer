using Otools.Net.Sfe.Core.Connections.Indexing;

namespace Otools.Net.Sfe.Api;

/// <summary>
/// For the lifetime of a library (one per library): drops the index rows of connections it no longer reaches
/// (removed, no longer offered to it), checked when the accounts' database changed; and writes what SFE changes in
/// connections into its index (<see cref="ConnectionIndexUpdates"/> listens from its start).
/// </summary>
public sealed class ConnectionIndexWorker(ConnectionIndexes indexes, ConnectionIndexUpdates updates) : BackgroundService
{
    /// <summary>How often the accounts' database is checked for a change.</summary>
    public static readonly TimeSpan CheckEvery = TimeSpan.FromSeconds(30);

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Resolved with the worker, so that it hears the changes from the library's start.
        GC.KeepAlive(updates);
        return indexes.WatchAsync(CheckEvery, stoppingToken);
    }
}
