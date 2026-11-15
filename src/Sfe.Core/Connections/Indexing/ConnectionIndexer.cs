using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Otools.Net.Sfe.Core.Data;

namespace Otools.Net.Sfe.Core.Connections.Indexing;

/// <summary>
/// Keeps a connection's rows in a library's index current from the provider's metadata (no file is downloaded):
/// a full listing the first time (<see cref="RemoteListingPass"/>, resumable page by page), then the changes since
/// the cursor taken when it began (<see cref="RemoteChangeReader"/>). A cursor the provider reset means a full
/// listing again, files matched by their provider ids so that they keep their tags; rows it did not see go at its
/// end. A provider without a change cursor is listed in full at each sync. Another root folder or another account
/// at the provider drops the rows first. One sync at a time per connection: the caller
/// (<see cref="ConnectionIndexes"/>) sees to it.
/// </summary>
public sealed class ConnectionIndexer(
    RemoteIndexTransactions store,
    RemoteIndexCleanup cleanup,
    TimeProvider clock,
    ILogger<ConnectionIndexer> logger
)
{
    private readonly RemoteIndexReport _report = new(logger);

    /// <summary>Brings the rows up to date; <see cref="RemoteStorageException"/> as the provider throws it.</summary>
    public async Task<ConnectionIndexResult> SyncAsync(
        ConnectionIndexTarget target,
        IRemoteIndexSource source,
        ConnectionIndexProgress progress,
        CancellationToken cancellationToken
    )
    {
        var state = await PrepareAsync(target, source, cancellationToken);
        var listed = false;
        // A provider forgetting its cursors at once again would list for ever: the second reset of a sync ends it.
        for (var resets = 0; ; resets++)
        {
            if (state.Phase == RemoteIndexPhase.Following)
            {
                if (!target.Capabilities.ChangeCursor)
                    state = await StartPassAsync(target, source, cancellationToken);
                else if (await Changes().RunAsync(target, source, state, progress, cancellationToken))
                    return await CompleteAsync(target, listed, progress, cancellationToken);
                else if (resets > 0)
                    throw new RemoteStorageException(
                        RemoteErrorKind.Unavailable,
                        "The provider reset its cursor again."
                    );
                else
                    state = await StartPassAsync(target, source, cancellationToken);
            }
            await new RemoteListingPass(store, _report, logger).RunAsync(
                target,
                source,
                state.Pass,
                progress,
                cancellationToken
            );
            listed = true;
            state = await FinishPassAsync(target, state.Pass, cancellationToken);
            if (!target.Capabilities.ChangeCursor)
                return await CompleteAsync(target, listed, progress, cancellationToken);
        }
    }

    private RemoteChangeReader Changes() => new(store, cleanup, _report, logger);

    /// <summary>The state to go on from; a new one (and a first listing) when there is none or it is another
    /// root folder's or account's.</summary>
    private async Task<RemoteIndexState> PrepareAsync(
        ConnectionIndexTarget target,
        IRemoteIndexSource source,
        CancellationToken cancellationToken
    )
    {
        var id = target.ConnectionId;
        var state = await store.StateAsync(id, cancellationToken);
        if (state is not null && (state.RootPath != target.RootPath || state.AccountKey != target.AccountKey))
        {
            logger.LogInformation(
                "Connection {ConnectionId} shows another folder or account now: its index is made again",
                id
            );
            await cleanup.ClearAsync(id, cancellationToken);
            state = null;
        }
        if (state is null)
        {
            logger.LogInformation("Connection {ConnectionId}: first listing", id);
            await store.WriteAsync(id, (db, _) => Begin(db, target), cancellationToken);
        }
        if (state is not { Pass: > 0 } going)
            return await StartPassAsync(target, source, cancellationToken);
        if (going.Phase == RemoteIndexPhase.Listing)
        {
            logger.LogInformation(
                "Connection {ConnectionId}: the listing pass {Pass} begun {Started:u} goes on where it stopped",
                id,
                going.Pass,
                going.ListingStarted
            );
        }
        return going;
    }

    private Task Begin(SfeDbContext db, ConnectionIndexTarget target)
    {
        db.RemoteIndexes.Add(
            new RemoteIndexState
            {
                ConnectionId = target.ConnectionId,
                RootPath = target.RootPath,
                AccountKey = target.AccountKey,
                Phase = RemoteIndexPhase.Listing,
                Created = clock.GetUtcNow().UtcDateTime,
            }
        );
        return Task.CompletedTask;
    }

    /// <summary>
    /// A new full listing: the change cursor taken first (what changes meanwhile comes after it), the root folder to
    /// list, nothing else.
    /// </summary>
    private async Task<RemoteIndexState> StartPassAsync(
        ConnectionIndexTarget target,
        IRemoteIndexSource source,
        CancellationToken cancellationToken
    )
    {
        var id = target.ConnectionId;
        var cursor = target.Capabilities.ChangeCursor
            ? (await source.ChangesAsync(null, cancellationToken)).Cursor
            : null;
        var now = clock.GetUtcNow().UtcDateTime;
        RemoteIndexState started = null!;
        await store.WriteAsync(
            id,
            async (db, token) =>
            {
                var state = await db.RemoteIndexes.FirstAsync(s => s.ConnectionId == id, token);
                state.Pass++;
                state.Phase = RemoteIndexPhase.Listing;
                state.ChangeCursor = cursor;
                state.CursorTaken = cursor is null ? null : now;
                state.ListingStarted = now;
                (state.WalkFolderId, state.WalkCursor, state.TooLong) = (null, null, 0);
                await db
                    .RemoteDirectories.Where(d => d.ConnectionId == id && d.Pending)
                    .ExecuteUpdateAsync(s => s.SetProperty(d => d.Pending, false), token);
                var scope = new RemoteIndexScope(id, target.CaseInsensitive, state.Pass, Following: false);
                (await new RemoteIndexWriter(db, scope).FolderAsync("", token)).Pending = true;
                started = state;
            },
            cancellationToken
        );
        logger.LogInformation("Connection {ConnectionId}: listing pass {Pass} begins", id, started.Pass);
        return started;
    }

    /// <summary>The listing ended: what it did not see goes, and the changes since it began come next.</summary>
    private async Task<RemoteIndexState> FinishPassAsync(
        ConnectionIndexTarget target,
        int pass,
        CancellationToken cancellationToken
    )
    {
        var id = target.ConnectionId;
        await cleanup.MarkUnseenAsync(id, pass, cancellationToken);
        await cleanup.PurgeRemovedAsync(id, cancellationToken);
        RemoteIndexState finished = null!;
        await store.WriteAsync(
            id,
            async (db, token) =>
            {
                finished = await db.RemoteIndexes.FirstAsync(s => s.ConnectionId == id, token);
                finished.Phase = RemoteIndexPhase.Following;
                (finished.WalkFolderId, finished.WalkCursor) = (null, null);
            },
            cancellationToken
        );
        return finished;
    }

    private async Task<ConnectionIndexResult> CompleteAsync(
        ConnectionIndexTarget target,
        bool listed,
        ConnectionIndexProgress progress,
        CancellationToken cancellationToken
    )
    {
        var id = target.ConnectionId;
        var (files, folders) = await cleanup.CountAsync(id, cancellationToken);
        await store.WriteAsync(
            id,
            async (db, token) =>
            {
                var state = await db.RemoteIndexes.FirstAsync(s => s.ConnectionId == id, token);
                (state.Files, state.Folders) = (files, folders);
                state.LastSync = clock.GetUtcNow().UtcDateTime;
                state.LastError = null;
            },
            cancellationToken
        );
        return new ConnectionIndexResult(files, folders, listed, progress.Changes, progress.TooLong);
    }
}
