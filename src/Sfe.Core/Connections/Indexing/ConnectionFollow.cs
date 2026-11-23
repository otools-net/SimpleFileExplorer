using Microsoft.Extensions.Logging;

namespace Otools.Net.Sfe.Core.Connections.Indexing;

/// <summary>
/// One quick look at a connection's changes for the folders watched in it (<see cref="ConnectionWatchers"/>): the
/// connection opened with its credentials, the changes since the index's cursor read through its gate and written
/// (<see cref="ConnectionIndexer.FollowAsync"/>), the server's kept listings of it forgotten when some came. No run of
/// the task: a failure is only logged (the next look, or the next sync, tells it); a refused sign-in marks it to be
/// signed in to again, as a sync does.
/// </summary>
internal sealed class ConnectionFollow(ConnectionIndexer indexer, ConnectionIndexSources sources, ILogger logger)
{
    /// <returns>What changed (null: nothing read), and whether the index must be synced instead: it doesn't follow
    /// the provider (never made, being listed, its cursor reset).</returns>
    public async Task<(ConnectionFollowResult? Result, bool MustSync)> RunAsync(
        int id,
        Func<ConnectionInfo, bool> reaches,
        CancellationToken cancellationToken
    )
    {
        var info = await sources.Connections.FindAsync(id, cancellationToken);
        if (info is null || info.Disabled || !reaches(info))
            return (null, false);
        if (sources.Providers.Find(info.Kind) is not { Capabilities.ChangeCursor: true } provider)
            return (null, false);
        var opened = await sources.Connections.OpenAsync(id, cancellationToken);
        if (opened.Value is not { } storage)
            return (null, false);

        await using (storage)
        {
            var target = ConnectionIndexTarget.Of(info, provider.Capabilities);
            var source = new GatedIndexSource(storage, sources.Calls, id);
            try
            {
                var result = await indexer.FollowAsync(target, source, cancellationToken);
                if (result is { Changes: > 0 })
                    sources.Listings.Forget(id);
                return (result, result is null);
            }
            catch (RemoteStorageException error)
            {
                if (error.Kind == RemoteErrorKind.CredentialsEnded)
                    await sources.Connections.MarkNeedsReconnectingAsync(id, CancellationToken.None);
                logger.LogDebug(
                    "Connection {ConnectionId}: its changes could not be read: {Kind} {Error}",
                    id,
                    error.Kind,
                    error.Message
                );
                return (null, false);
            }
        }
    }
}
