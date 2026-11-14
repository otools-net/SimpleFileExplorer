using Sfe.Core.Library;

namespace Sfe.Core.Connections;

/// <summary>
/// Pages of a connection's folders. Providers list in their own order and page size, so sorting needs the whole
/// folder: the first page (offset 0) reads it once into a snapshot and sorts it, the next pages are taken from it
/// while it is recent. A folder of more than <see cref="MaxItems"/> items is listed in the provider's order with
/// sorting off; its snapshot keeps a window of at most that many items and reads forward as the pages go.
/// </summary>
/// <remarks>
/// Memory: at most <see cref="MaxSnapshots"/> snapshots of at most <see cref="MaxItems"/> items, whatever the number
/// of connections. A snapshot is the provider's account seen by everyone who uses the connection (folder rules come
/// later and filter the page, not the snapshot).
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
    private readonly List<Snapshot> _snapshots = [];

    private sealed record SnapshotKey(int ConnectionId, string Path, SortField Sort, bool Descending);

    /// <param name="Start">Offset of the first item kept (above 0 once an unsorted window has moved on).</param>
    /// <param name="Cursor">The provider's cursor of the next page; null once the folder is read whole.</param>
    private sealed class Snapshot(SnapshotKey key, DateTimeOffset read)
    {
        public SnapshotKey Key { get; } = key;
        public DateTimeOffset Read { get; set; } = read;
        public List<RemoteItem> Items { get; set; } = [];
        public int Start { get; set; }
        public string? Cursor { get; set; }
        public bool Sorted { get; set; }
        public int Folders { get; set; }
        public SemaphoreSlim Gate { get; } = new(1, 1);
    }

    /// <summary>A page of a folder; <see cref="RemoteStorageException"/> as the provider throws it.</summary>
    public async Task<RemoteListing> ListAsync(
        IRemoteStorage storage,
        RemoteListingQuery query,
        CancellationToken cancellationToken
    )
    {
        var key = new SnapshotKey(query.ConnectionId, query.Path, query.Sort, query.Descending);
        var snapshot = Take(key);
        await snapshot.Gate.WaitAsync(cancellationToken);
        try
        {
            var stale = clock.GetUtcNow() - snapshot.Read > Lifetime;
            if (query.Offset == 0 || stale || query.Offset < snapshot.Start || snapshot.Read == default)
                await ReadAnewAsync(storage, snapshot, cancellationToken);
            // One item past the page: the client sees there is more (offset + items < total).
            if (snapshot.Cursor is not null)
                await ReadOnAsync(storage, snapshot, (query.Offset, query.Offset + query.Limit + 1), cancellationToken);
            return PageOf(snapshot, query);
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

    private Snapshot Take(SnapshotKey key)
    {
        lock (_gate)
        {
            var index = _snapshots.FindIndex(s => s.Key == key);
            var snapshot = index >= 0 ? _snapshots[index] : new Snapshot(key, default);
            if (index >= 0)
                _snapshots.RemoveAt(index);
            _snapshots.Add(snapshot);
            if (_snapshots.Count > MaxSnapshots)
                _snapshots.RemoveAt(0);
            return snapshot;
        }
    }

    private async Task ReadAnewAsync(IRemoteStorage storage, Snapshot snapshot, CancellationToken cancellationToken)
    {
        var items = new List<RemoteItem>();
        string? cursor = null;
        do
        {
            var page = await storage.ListAsync(snapshot.Key.Path, cursor, cancellationToken);
            items.AddRange(page.Items);
            cursor = page.Cursor;
        } while (cursor is not null && items.Count < maxItems);

        snapshot.Read = clock.GetUtcNow();
        snapshot.Start = 0;
        snapshot.Cursor = cursor;
        snapshot.Sorted = cursor is null;
        snapshot.Items = snapshot.Sorted ? RemoteSort.Sort(items, snapshot.Key.Sort, snapshot.Key.Descending) : items;
        snapshot.Folders = items.Count(i => i.IsFolder);
    }

    /// <summary>Reads the next pages of an unsorted folder; the window passes the cap by a page at most.</summary>
    /// <param name="wanted">The page's first item, kept in the window, and the item to read up to.</param>
    private async Task ReadOnAsync(
        IRemoteStorage storage,
        Snapshot snapshot,
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
            if (over > 0)
            {
                // The oldest items leave the window: asking for them again reads the folder anew.
                snapshot.Items.RemoveRange(0, over);
                snapshot.Start += over;
            }
        }
    }

    private static RemoteListing PageOf(Snapshot snapshot, RemoteListingQuery query)
    {
        var from = Math.Max(0, query.Offset - snapshot.Start);
        var items = snapshot.Items.Skip(from).Take(query.Limit).ToList();
        return new RemoteListing(
            items,
            query.Offset,
            snapshot.Start + snapshot.Items.Count,
            snapshot.Folders,
            snapshot.Sorted,
            snapshot.Cursor is not null
        );
    }
}
