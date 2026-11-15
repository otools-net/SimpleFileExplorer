namespace Otools.Net.Sfe.Core.Connections.Indexing;

/// <summary>
/// The provider calls a sync makes (<see cref="ConnectionIndexer"/>): through <see cref="Transfers.TransferCalls"/>
/// in the server (turns, waits, retries: <see cref="GatedIndexSource"/>), straight to the storage in tests.
/// </summary>
public interface IRemoteIndexSource
{
    /// <inheritdoc cref="IRemoteStorage.ListAsync"/>
    Task<RemotePage> ListAsync(string path, string? cursor, CancellationToken cancellationToken);

    /// <inheritdoc cref="IRemoteStorage.ChangesAsync"/>
    Task<RemoteChanges> ChangesAsync(string? cursor, CancellationToken cancellationToken);
}
