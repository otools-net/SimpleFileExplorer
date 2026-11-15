using Microsoft.Extensions.Logging;

namespace Otools.Net.Sfe.Core.Connections.Indexing;

/// <summary>
/// Changes SFE made in a connection (<see cref="ConnectionChanges"/>) written at once into this library's rows of
/// it, when the library indexes it: a folder made, a file uploaded or copied there, an item renamed or moved (a
/// folder's rows follow it, files keep their tags), an item deleted or moved out (marked removed, dropped when the
/// next sync ends). A server-side copy of a folder brings its folder only: what is in it comes with the next sync.
/// </summary>
public sealed class ConnectionIndexUpdates : IDisposable
{
    private readonly RemoteIndexTransactions _store;
    private readonly ILogger<ConnectionIndexUpdates> _logger;
    private readonly IDisposable? _listening;

    /// <param name="sources">The server's (null in the Core's own tests: nothing is heard).</param>
    public ConnectionIndexUpdates(
        RemoteIndexTransactions store,
        ILogger<ConnectionIndexUpdates> logger,
        ConnectionIndexSources? sources = null
    )
    {
        (_store, _logger) = (store, logger);
        _listening = sources?.Changes.Listen(ApplyAsync);
    }

    public async Task ApplyAsync(ConnectionItemChange change, CancellationToken cancellationToken)
    {
        var id = change.ConnectionId;
        if (await _store.StateAsync(id, cancellationToken) is not { } state)
            return;
        // A folder copied by the provider is still to list (its items come with the next sync); anything else SFE
        // made is complete.
        var complete = change.Kind != ConnectionItemChangeKind.Copied;
        var scope = new RemoteIndexScope(id, change.Capabilities.CaseInsensitiveNames, state.Pass, complete);
        List<string> tooLong = [];
        await _store.WriteAsync(
            id,
            async (db, token) =>
            {
                var writer = new RemoteIndexWriter(db, scope);
                await WriteAsync(db, writer, (scope, change), token);
                tooLong = writer.TooLong;
            },
            cancellationToken
        );
        foreach (var path in tooLong)
            _logger.LogDebug("Connection {ConnectionId}: left out of the index (path too long): {Path}", id, path);
    }

    private static async Task WriteAsync(
        Data.SfeDbContext db,
        RemoteIndexWriter writer,
        (RemoteIndexScope Scope, ConnectionItemChange Change) what,
        CancellationToken cancellationToken
    )
    {
        var (scope, change) = what;
        if (change.Item is not { } item)
        {
            await writer.RemoveAsync(change.Path, cancellationToken);
            return;
        }
        var moved = change.Kind == ConnectionItemChangeKind.Moved;
        if (moved && item.IsFolder)
            await RemoteTreeMoves.MoveAsync(db, scope, (change.Path, item), cancellationToken);
        await writer.LoadAsync([item], cancellationToken);
        // A file moved: its row is found by its id (tags kept); one without an id leaves its old row removed.
        if (moved && !item.IsFolder && !SameKey(scope, change.Path, item.Path))
            await writer.RemoveAsync(change.Path, cancellationToken);
        await writer.PutAsync(item, cancellationToken);
    }

    private static bool SameKey(RemoteIndexScope scope, string one, string other) =>
        RemoteIndexKeys.Key(one, scope.CaseInsensitive) == RemoteIndexKeys.Key(other, scope.CaseInsensitive);

    public void Dispose() => _listening?.Dispose();
}
