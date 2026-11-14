using Otools.Net.Sfe.Core.Library;

namespace Otools.Net.Sfe.Core.Connections;

/// <summary>
/// Pages of a connection's folders. Providers list in their own order and page size, so sorting needs the whole
/// folder: the first page (offset 0) reads it once into a snapshot and sorts it, the next pages are taken from it
/// while it is recent. A folder of more than <see cref="MaxItems"/> items is listed in the provider's order with
/// sorting off; its snapshot keeps a window of at most that many items and reads forward as the pages go.
/// </summary>
/// <remarks>
/// Memory: at most <see cref="MaxSnapshots"/> snapshots of at most <see cref="MaxItems"/> items, whatever the number
/// of connections. A snapshot is the provider's account seen by everyone who uses the connection; folder rules
/// filter the page for each subject (<see cref="RemoteSnapshot"/>), never the snapshot.
/// </remarks>
/// <param name="maxItems">Tests make it small; the server keeps <see cref="MaxItems"/>.</param>
public sealed class RemoteListings(TimeProvider clock, int maxItems = RemoteListings.MaxItems)
{
    /// <summary>Items a snapshot keeps; past it a folder is listed unsorted.</summary>
    public const int MaxItems = 20_000;

    /// <summary>Snapshots kept, most recently used last.</summary>
    public const int MaxSnapshots = 16;

    /// <summary>How long the next pages reuse a snapshot.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);

    private readonly Lock _gate = new();
    private readonly List<RemoteSnapshot> _snapshots = [];

    /// <summary>A page of a folder; <see cref="RemoteStorageException"/> as the provider throws it.</summary>
    public Task<RemoteListing> ListAsync(
        IRemoteStorage storage,
        RemoteListingQuery query,
        CancellationToken cancellationToken
    ) => ListAsync(storage, query, null, cancellationToken);

    /// <summary>
    /// A page of a folder without what <paramref name="filter"/> hides (folder rules): offsets, totals and counts
    /// are those of what is left. A folder only leading to what the subject sees (<see cref="ListingFilter.Only"/>)
    /// is not listed: the few children leading on are asked one by one.
    /// </summary>
    public async Task<RemoteListing> ListAsync(
        IRemoteStorage storage,
        RemoteListingQuery query,
        ListingFilter? filter,
        CancellationToken cancellationToken
    )
    {
        if (filter?.Only is { } only)
            return await ListOnlyAsync(storage, query, only.Where(filter.Shows).ToList(), cancellationToken);

        var key = new RemoteSnapshotKey(query.ConnectionId, query.Path, query.Sort, query.Descending);
        var snapshot = Take(key);
        await snapshot.Gate.WaitAsync(cancellationToken);
        try
        {
            if (MustReadAnew(snapshot, query, filter))
                await ReadAnewAsync(storage, snapshot, cancellationToken);
            snapshot.Watch(filter);
            // One item past the page: the client sees there is more (offset + items < total). The hidden ones are
            // read past too.
            var end = query.Offset + query.Limit + 1 + (filter?.Hidden.Count ?? 0);
            if (snapshot.Cursor is not null)
                await ReadOnAsync(storage, snapshot, (query.Offset, end), cancellationToken);
            return snapshot.PageOf(query, filter);
        }
        finally
        {
            snapshot.Gate.Release();
        }
    }

    /// <summary>Drops the snapshots of a connection: its files changed through SFE, or it was removed.</summary>
    public void Forget(int connectionId)
    {
        lock (_gate)
            _snapshots.RemoveAll(s => s.Key.ConnectionId == connectionId);
    }

    private bool MustReadAnew(RemoteSnapshot snapshot, RemoteListingQuery query, ListingFilter? filter) =>
        query.Offset == 0
        || snapshot.Read == default
        || clock.GetUtcNow() - snapshot.Read > Lifetime
        || query.Offset < snapshot.Start
        || !snapshot.Knows(filter);

    /// <summary>The children named, as the provider has them, sorted and paged.</summary>
    private static async Task<RemoteListing> ListOnlyAsync(
        IRemoteStorage storage,
        RemoteListingQuery query,
        IReadOnlyList<string> names,
        CancellationToken cancellationToken
    )
    {
        var items = new List<RemoteItem>();
        foreach (var name in names)
        {
            if (await storage.GetAsync(RemotePaths.Combine(query.Path, name), cancellationToken) is { } item)
                items.Add(item);
        }
        var sorted = RemoteSort.Sort(items, query.Sort, query.Descending);
        var page = sorted.Skip(query.Offset).Take(query.Limit).ToList();
        return new RemoteListing(page, query.Offset, sorted.Count, sorted.Count(i => i.IsFolder), true, false);
    }

    private RemoteSnapshot Take(RemoteSnapshotKey key)
    {
        lock (_gate)
        {
            var index = _snapshots.FindIndex(s => s.Key == key);
            var snapshot = index >= 0 ? _snapshots[index] : new RemoteSnapshot(key);
            if (index >= 0)
                _snapshots.RemoveAt(index);
            _snapshots.Add(snapshot);
            if (_snapshots.Count > MaxSnapshots)
                _snapshots.RemoveAt(0);
            return snapshot;
        }
    }

    private async Task ReadAnewAsync(
        IRemoteStorage storage,
        RemoteSnapshot snapshot,
        CancellationToken cancellationToken
    )
    {
        var items = new List<RemoteItem>();
        string? cursor = null;
        do
        {
            var page = await storage.ListAsync(snapshot.Key.Path, cursor, cancellationToken);
            items.AddRange(page.Items);
            cursor = page.Cursor;
        } while (cursor is not null && items.Count < maxItems);

        snapshot.Reset(items, cursor, clock.GetUtcNow());
    }

    /// <summary>Reads the next pages of an unsorted folder; the window passes the cap by a page at most.</summary>
    /// <param name="wanted">The page's first item, kept in the window, and the item to read up to.</param>
    private async Task ReadOnAsync(
        IRemoteStorage storage,
        RemoteSnapshot snapshot,
        (int From, int End) wanted,
        CancellationToken cancellationToken
    )
    {
        while (snapshot.Cursor is { } cursor && snapshot.Start + snapshot.Items.Count < wanted.End)
        {
            var page = await storage.ListAsync(snapshot.Key.Path, cursor, cancellationToken);
            snapshot.Items.AddRange(page.Items);
            snapshot.Folders += page.Items.Count(i => i.IsFolder);
            snapshot.Cursor = page.Cursor;
            var over = Math.Min(snapshot.Items.Count - maxItems, wanted.From - snapshot.Start);
            // The oldest items leave the window: asking for them again reads the folder anew.
            if (over > 0)
                snapshot.Drop(over);
        }
    }
}
