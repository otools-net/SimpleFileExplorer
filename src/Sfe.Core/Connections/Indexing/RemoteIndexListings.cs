using Microsoft.EntityFrameworkCore;
using Otools.Net.Sfe.Core.Data;

namespace Otools.Net.Sfe.Core.Connections.Indexing;

/// <summary>
/// Pages of a connection's folder read from the library's index instead of the provider, when the index holds the
/// whole folder (<see cref="CompleteFolderAsync"/>); otherwise the caller lists it from the provider
/// (<see cref="RemoteListings"/>) and <see cref="IndexedAsync"/> tells which of its items the index has. Sorted by
/// the database (folders first, names in natural order, all in the sort's direction: <see cref="IndexListingOrder"/>),
/// counted, and paged by key: each page reads after the last row of the page before
/// (<see cref="IndexListingPlaces"/>), so scrolling a folder of a million items costs a page at a time; the counts
/// are read with the first page.
/// </summary>
/// <remarks>
/// Rows marked removed are never read. Folder rules filter the rows in the queries (<see cref="IndexListingFilter"/>):
/// a hidden child is in no page, count or later page.
/// </remarks>
public sealed class RemoteIndexListings(RemoteIndexTransactions store, IndexListingPlaces places, TimeProvider clock)
{
    /// <summary>
    /// An index synced longer ago is not listed from (the server was off, the syncs failed): the provider is read
    /// while the sync that opening the folder started catches up.
    /// </summary>
    public static readonly TimeSpan MaxAge = ConnectionIndexes.SyncAfter * 3;

    /// <summary>A page of the folder from the index; null when the index doesn't hold it whole.</summary>
    public async Task<RemoteListing?> ListAsync(IndexListingQuery asked, CancellationToken cancellationToken)
    {
        await using var db = await store.ContextAsync(cancellationToken);
        if (await CompleteFolderAsync(db, asked, cancellationToken) is not { } folder)
            return null;
        var query = asked.Query;
        var filter = new IndexListingFilter(asked, folder.Key);
        var key = new IndexListingKey(
            query.ConnectionId,
            folder.Id,
            query.Sort,
            query.Descending,
            filter.Signature,
            query.Offset
        );
        var place = query.Offset > 0 ? places.Find(key) : null;
        if (place is null && query.Offset > 0 && asked.ProviderPaging)
            return null;

        var rows = new IndexListingRows(db, asked, folder.Id, filter);
        var counts = place?.Counts ?? await rows.CountAsync(cancellationToken);
        // One row past the page: whether the folder goes on, whatever the counts read with the first page say.
        var read = await rows.PageAsync(
            (query.Offset, query.Limit + 1),
            place?.Last,
            counts.Folders,
            cancellationToken
        );
        var shown = read.Take(query.Limit).ToList();
        var more = read.Count > query.Limit;
        if (more)
            places.Keep(key with { Offset = query.Offset + shown.Count }, shown[^1], counts);
        var total =
            more ? Math.Max(counts.Total, query.Offset + shown.Count + 1)
            : shown.Count > 0 ? query.Offset + shown.Count
            : counts.Total;
        var items = shown.Select(r => r.ToItem()).ToList();
        return new RemoteListing(items, query.Offset, total, Math.Min(counts.Folders, total), true, false);
    }

    /// <summary>
    /// The paths of <paramref name="items"/> (read from the provider) the index has: files, and folders listed
    /// whole. Null when the connection has no index in this library (nothing is marked).
    /// </summary>
    public async Task<IReadOnlySet<string>?> IndexedAsync(
        ConnectionIndexTarget target,
        IReadOnlyList<RemoteItem> items,
        CancellationToken cancellationToken
    )
    {
        await using var db = await store.ContextAsync(cancellationToken);
        if (await StateAsync(db, target, cancellationToken) is null)
            return null;
        var id = target.ConnectionId;
        var byKey = items
            .Where(i => !RemoteIndexKeys.IsTooLong(i.Path))
            .GroupBy(i => RemoteIndexKeys.Key(i.Path, target.CaseInsensitive))
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
        var files = byKey.Where(e => e.Value.Any(i => !i.IsFolder)).Select(e => e.Key).ToList();
        var folders = byKey.Where(e => e.Value.Any(i => i.IsFolder)).Select(e => e.Key).ToList();
        var known = new List<string>();
        if (files.Count > 0)
        {
            known.AddRange(
                await db
                    .RemoteFiles.Where(f => f.ConnectionId == id && files.Contains(f.FullName) && !f.Removed)
                    .Select(f => f.FullName)
                    .ToListAsync(cancellationToken)
            );
        }
        if (folders.Count > 0)
        {
            known.AddRange(
                await db
                    .RemoteDirectories.Where(d =>
                        d.ConnectionId == id && folders.Contains(d.FullName) && !d.Removed && d.Listed
                    )
                    .Select(d => d.FullName)
                    .ToListAsync(cancellationToken)
            );
        }
        return known.SelectMany(k => byKey[k]).Select(i => i.Path).ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>
    /// The folder's row when the index holds it whole: the index is of the connection's root folder and account,
    /// past its full listing (<see cref="RemoteIndexPhase.Following"/>: during one, rows the listing has not met
    /// again may be gone at the provider), synced within <see cref="MaxAge"/>, and the folder itself listed to its
    /// end (or made through SFE, or brought by the changes with its items), not removed, its rows' name keys filled.
    /// </summary>
    private async Task<(long Id, string Key)?> CompleteFolderAsync(
        SfeDbContext db,
        IndexListingQuery asked,
        CancellationToken cancellationToken
    )
    {
        var target = asked.Target;
        var state = await StateAsync(db, target, cancellationToken);
        if (state is not { Phase: RemoteIndexPhase.Following, LastSync: { } synced })
            return null;
        if (clock.GetUtcNow() - new DateTimeOffset(synced, TimeSpan.Zero) > MaxAge)
            return null;
        var id = target.ConnectionId;
        var key = RemoteIndexKeys.Key(asked.Query.Path, target.CaseInsensitive);
        var folder = await db
            .RemoteDirectories.AsNoTracking()
            .Where(d => d.ConnectionId == id && d.FullName == key && !d.Removed && d.Listed && !d.Pending)
            .Select(d => new { d.Id, d.FullName })
            .FirstOrDefaultAsync(cancellationToken);
        if (folder is null || await LacksNameKeysAsync(db, id, folder.Id, cancellationToken))
            return null;
        return (folder.Id, folder.FullName);
    }

    /// <summary>Rows of the folder indexed before their name key existed, not filled yet by a sync.</summary>
    private static async Task<bool> LacksNameKeysAsync(
        SfeDbContext db,
        int id,
        long folderId,
        CancellationToken cancellationToken
    ) =>
        await db.RemoteFiles.AnyAsync(
            f => f.ConnectionId == id && f.DirectoryId == folderId && f.NameKey == null,
            cancellationToken
        )
        || await db.RemoteDirectories.AnyAsync(
            d => d.ConnectionId == id && d.ParentId == folderId && d.NameKey == null,
            cancellationToken
        );

    /// <summary>The connection's index state, when it is of the connection's root folder and account.</summary>
    private static async Task<RemoteIndexState?> StateAsync(
        SfeDbContext db,
        ConnectionIndexTarget target,
        CancellationToken cancellationToken
    )
    {
        var id = target.ConnectionId;
        var state = await db
            .RemoteIndexes.AsNoTracking()
            .FirstOrDefaultAsync(s => s.ConnectionId == id, cancellationToken);
        return state is not null && state.RootPath == target.RootPath && state.AccountKey == target.AccountKey
            ? state
            : null;
    }
}
