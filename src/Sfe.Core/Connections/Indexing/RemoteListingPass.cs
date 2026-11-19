using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Otools.Net.Sfe.Core.Data;

namespace Otools.Net.Sfe.Core.Connections.Indexing;

/// <summary>
/// A full listing of a connection, folder by folder: each page of the provider in one transaction with where the
/// listing is (<see cref="RemoteIndexState.WalkFolderId"/>, <see cref="RemoteIndexState.WalkCursor"/>), so a restart
/// goes on from the last page written. The folders still to list are rows (<see cref="RemoteDirectory.Pending"/>):
/// memory holds one page.
/// </summary>
internal sealed class RemoteListingPass(RemoteIndexTransactions store, RemoteIndexReport report, ILogger logger)
{
    public async Task RunAsync(
        ConnectionIndexTarget target,
        IRemoteIndexSource source,
        int pass,
        ConnectionIndexProgress progress,
        CancellationToken cancellationToken
    )
    {
        progress.Listing = true;
        while (await NextFolderAsync(target.ConnectionId, cancellationToken) is { } next)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var listing = System.Diagnostics.Stopwatch.StartNew();
            var page = await ListAsync(target, source, next, cancellationToken);
            var listed = listing.Elapsed;
            if (page is null)
            {
                await GoneAsync(target, pass, next.Folder, cancellationToken);
                continue;
            }
            await WriteAsync(target, pass, (next.Folder, page), progress, cancellationToken);
            report.Page(target.ConnectionId, page.Items.Count, (listed, listing.Elapsed - listed));
        }
    }

    /// <summary>The folder the listing stopped in (and its next page), else the first one still to list.</summary>
    private async Task<(RemoteDirectory Folder, string? Cursor)?> NextFolderAsync(int id, CancellationToken token)
    {
        var state = await store.StateAsync(id, token);
        await using var db = await store.ContextAsync(token);
        var folders = db.RemoteDirectories.AsNoTracking().Where(d => d.ConnectionId == id && d.Pending && !d.Removed);
        if (
            state?.WalkFolderId is { } stopped
            && await folders.FirstOrDefaultAsync(d => d.Id == stopped, token) is { } at
        )
        {
            return (at, state.WalkCursor);
        }

        var first = await folders.OrderBy(d => d.Id).FirstOrDefaultAsync(token);
        return first is null ? null : (first, null);
    }

    /// <summary>A page of the folder; null when the folder is gone. A page cursor the provider no longer knows (a
    /// listing resumed much later) lists the folder again from its start.</summary>
    private async Task<RemotePage?> ListAsync(
        ConnectionIndexTarget target,
        IRemoteIndexSource source,
        (RemoteDirectory Folder, string? Cursor) next,
        CancellationToken cancellationToken
    )
    {
        var path = next.Folder.DisplayPath;
        try
        {
            return await source.ListAsync(path, next.Cursor, cancellationToken);
        }
        catch (RemoteStorageException error) when (error.Kind == RemoteErrorKind.NotFound && next.Cursor is null)
        {
            return null;
        }
        catch (RemoteStorageException error) when (next.Cursor is not null && IsCursorRefused(error.Kind))
        {
            logger.LogInformation(
                "Connection {ConnectionId}: the provider no longer knows where the listing of a folder was; it is "
                    + "listed again from its start ({Kind})",
                target.ConnectionId,
                error.Kind
            );
            return await ListAsync(target, source, (next.Folder, null), cancellationToken);
        }
    }

    private static bool IsCursorRefused(RemoteErrorKind kind) =>
        kind is RemoteErrorKind.NotFound or RemoteErrorKind.Refused or RemoteErrorKind.NotSupported;

    private async Task WriteAsync(
        ConnectionIndexTarget target,
        int pass,
        (RemoteDirectory Folder, RemotePage Page) listed,
        ConnectionIndexProgress progress,
        CancellationToken cancellationToken
    )
    {
        var (folderId, page) = (listed.Folder.Id, listed.Page);
        var done = page.Cursor is null;
        List<string> tooLong = [];
        await store.WriteAsync(
            target.ConnectionId,
            async (db, token) =>
            {
                var state = await db.RemoteIndexes.FirstAsync(s => s.ConnectionId == target.ConnectionId, token);
                var writer = new RemoteIndexWriter(db, ScopeOf(target, pass));
                await writer.LoadAsync(page.Items, token);
                foreach (var item in page.Items)
                    await writer.PutAsync(item, token);
                var folder = await db.RemoteDirectories.FirstAsync(d => d.Id == folderId, token);
                folder.Pending = !done;
                folder.Listed |= done;
                state.WalkFolderId = done ? null : folderId;
                state.WalkCursor = page.Cursor;
                state.TooLong += writer.TooLong.Count;
                tooLong = writer.TooLong;
            },
            cancellationToken
        );
        progress.Listed(done ? 1 : 0, page.Items.Count(i => !i.IsFolder));
        report.TooLong(target, tooLong, progress);
    }

    /// <summary>The folder went away at the provider while it waited its turn: it and what is below it go.</summary>
    private Task GoneAsync(ConnectionIndexTarget target, int pass, RemoteDirectory gone, CancellationToken token) =>
        store.WriteAsync(
            target.ConnectionId,
            async (db, inner) =>
            {
                var state = await db.RemoteIndexes.FirstAsync(s => s.ConnectionId == target.ConnectionId, inner);
                var folder = await db.RemoteDirectories.FirstAsync(d => d.Id == gone.Id, inner);
                folder.Pending = false;
                state.WalkFolderId = null;
                state.WalkCursor = null;
                await new RemoteIndexWriter(db, ScopeOf(target, pass)).RemoveAsync(gone.DisplayPath, inner);
            },
            token
        );

    private static RemoteIndexScope ScopeOf(ConnectionIndexTarget target, int pass) =>
        new(target.ConnectionId, target.CaseInsensitive, pass, Following: false);
}
