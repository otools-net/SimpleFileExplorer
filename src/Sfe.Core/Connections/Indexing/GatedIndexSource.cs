using Otools.Net.Sfe.Core.Transfers;

namespace Otools.Net.Sfe.Core.Connections.Indexing;

/// <summary>
/// A sync's calls through <see cref="TransferCalls"/>: each in a turn of the connection's gate, waiting while the
/// provider asks to, tried again while it is down; so a sync never takes the turns people's requests need.
/// </summary>
public sealed class GatedIndexSource(IRemoteStorage storage, TransferCalls calls, int connectionId) : IRemoteIndexSource
{
    public Task<RemotePage> ListAsync(string path, string? cursor, CancellationToken cancellationToken) =>
        calls.RunAsync(connectionId, token => storage.ListAsync(path, cursor, token), cancellationToken);

    public Task<RemoteChanges> ChangesAsync(string? cursor, CancellationToken cancellationToken) =>
        calls.RunAsync(connectionId, token => storage.ChangesAsync(cursor, token), cancellationToken);
}
