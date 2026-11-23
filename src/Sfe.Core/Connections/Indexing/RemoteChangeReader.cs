using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Otools.Net.Sfe.Core.Data;

namespace Otools.Net.Sfe.Core.Connections.Indexing;

/// <summary>
/// Reads what changed at the provider since the stored cursor, page by page, each page applied in one transaction
/// with the next cursor (a restart asks from the last page written). Applied in the provider's order: an item as it
/// is now (a file found by its id first, so a rename keeps its row and its tags), or gone with what was below it.
/// The folders whose listings changed are noted (<see cref="Folders"/>), for those watching them.
/// </summary>
internal sealed class RemoteChangeReader(
    RemoteIndexTransactions store,
    RemoteIndexCleanup cleanup,
    RemoteIndexReport report,
    ILogger logger
)
{
    /// <summary>The most folders noted; past it <see cref="TooManyFolders"/> (a whole tree changed).</summary>
    public const int MaxFolders = 1000;

    private readonly HashSet<string> _folders = new(StringComparer.Ordinal);

    /// <summary>
    /// The rows removed are deleted at the end even when no change came (SFE's own deletes mark rows); a watched
    /// folder's quick look leaves them to a read that brings changes, as that delete comes back from the provider.
    /// </summary>
    public bool PurgeWhenUnchanged { get; init; } = true;

    /// <summary>The folders (from the root) whose items changed, and the folders changed themselves.</summary>
    public IReadOnlyCollection<string> Folders => _folders;

    /// <summary>
    /// More than <see cref="MaxFolders"/> folders changed: <see cref="Folders"/> doesn't name them all.
    /// </summary>
    public bool TooManyFolders { get; private set; }

    /// <returns>False when the provider no longer knows the cursor (reset): everything must be listed again.</returns>
    public async Task<bool> RunAsync(
        ConnectionIndexTarget target,
        IRemoteIndexSource source,
        RemoteIndexState state,
        ConnectionIndexProgress progress,
        CancellationToken cancellationToken
    )
    {
        progress.Listing = false;
        if (state.ChangeCursor is not { } cursor)
            return false;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var reading = System.Diagnostics.Stopwatch.StartNew();
            var changes = await source.ChangesAsync(cursor, cancellationToken);
            var read = reading.Elapsed;
            if (changes.Reset)
            {
                LogReset(target, state);
                return false;
            }
            await WriteAsync(target, state.Pass, changes, progress, cancellationToken);
            Note(changes.Changes);
            report.Page(target.ConnectionId, changes.Changes.Count, (read, reading.Elapsed - read));
            cursor = changes.Cursor;
            if (!changes.HasMore)
                break;
        }
        if (PurgeWhenUnchanged || progress.Changes > 0)
            await cleanup.PurgeRemovedAsync(target.ConnectionId, cancellationToken);
        return true;
    }

    private void Note(IReadOnlyList<RemoteChange> changes)
    {
        foreach (var change in changes)
        {
            if (_folders.Count >= MaxFolders)
            {
                TooManyFolders = true;
                return;
            }
            _folders.Add(RemotePaths.Parent(change.Path));
            if (change.Item is null or { IsFolder: true })
                _folders.Add(change.Path);
        }
    }

    private void LogReset(ConnectionIndexTarget target, RemoteIndexState state)
    {
        var age = state.CursorTaken is { } taken ? DateTime.UtcNow - taken : (TimeSpan?)null;
        logger.LogWarning(
            "Connection {ConnectionId}: the provider reset the change cursor taken {Taken:u} ({AgeDays:N1} days "
                + "old); everything is listed again (files keep their tags by their ids)",
            target.ConnectionId,
            state.CursorTaken,
            age?.TotalDays
        );
    }

    private async Task WriteAsync(
        ConnectionIndexTarget target,
        int pass,
        RemoteChanges changes,
        ConnectionIndexProgress progress,
        CancellationToken cancellationToken
    )
    {
        List<string> tooLong = [];
        await store.WriteAsync(
            target.ConnectionId,
            async (db, token) =>
            {
                var state = await db.RemoteIndexes.FirstAsync(s => s.ConnectionId == target.ConnectionId, token);
                var writer = new RemoteIndexWriter(
                    db,
                    new(target.ConnectionId, target.CaseInsensitive, pass, Following: true)
                );
                await writer.LoadAsync([.. changes.Changes.Select(c => c.Item).OfType<RemoteItem>()], token);
                foreach (var change in changes.Changes)
                    await writer.ApplyAsync(change, token);
                state.ChangeCursor = changes.Cursor;
                state.TooLong += writer.TooLong.Count;
                tooLong = writer.TooLong;
            },
            cancellationToken
        );
        progress.Changed(changes.Changes.Count);
        report.TooLong(target, tooLong, progress);
    }
}
