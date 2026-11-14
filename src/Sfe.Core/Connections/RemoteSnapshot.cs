using Sfe.Core.Library;

namespace Sfe.Core.Connections;

/// <summary>
/// One read of a connection's folder kept by <see cref="RemoteListings"/>, shared by everyone who lists it, and its
/// pages for one subject: what folder rules hide from it left out, offsets and counts those of what is left.
/// </summary>
/// <remarks>
/// A folder too large to keep whole is a window that moves on: an item leaving it is forgotten, but the hidden ones
/// change the offsets of what comes after. So the names some filter hid are watched, and a watched item leaving the
/// window is remembered with its place (bounded: a folder holds a name once, and filters name only the few children
/// rules are about). A filter naming what wasn't watched when the window moved needs the folder read anew.
/// </remarks>
internal sealed class RemoteSnapshot(RemoteSnapshotKey key)
{
    /// <summary>Names watched, ignoring case: over-watching only remembers a few more items.</summary>
    private readonly HashSet<string> _watched = new(StringComparer.OrdinalIgnoreCase);

    private readonly List<(int Index, string Name, bool IsFolder)> _left = [];

    public RemoteSnapshotKey Key { get; } = key;

    /// <summary>When it was read; default before the first read.</summary>
    public DateTimeOffset Read { get; set; }

    public List<RemoteItem> Items { get; private set; } = [];

    /// <summary>Offset of the first item kept (above 0 once an unsorted window has moved on).</summary>
    public int Start { get; private set; }

    /// <summary>The provider's cursor of the next page; null once the folder is read whole.</summary>
    public string? Cursor { get; set; }

    public bool Sorted { get; private set; }

    public int Folders { get; set; }

    public SemaphoreSlim Gate { get; } = new(1, 1);

    /// <summary>Starts again from a new read of the folder's first items.</summary>
    public void Reset(List<RemoteItem> items, string? cursor, DateTimeOffset read)
    {
        Read = read;
        Start = 0;
        Cursor = cursor;
        Sorted = cursor is null;
        Items = Sorted ? RemoteSort.Sort(items, Key.Sort, Key.Descending) : items;
        Folders = items.Count(i => i.IsFolder);
        _watched.Clear();
        _left.Clear();
    }

    /// <summary>The window holds what <paramref name="filter"/> needs to count what it hides before it.</summary>
    public bool Knows(ListingFilter? filter) => filter is null || Start == 0 || filter.Hidden.All(_watched.Contains);

    public void Watch(ListingFilter? filter)
    {
        if (filter is not null)
            _watched.UnionWith(filter.Hidden);
    }

    /// <summary>The first <paramref name="count"/> items leave the window; watched ones are remembered.</summary>
    public void Drop(int count)
    {
        for (var i = 0; i < count; i++)
        {
            var item = Items[i];
            if (_watched.Contains(item.Name))
                _left.Add((Start + i, item.Name, item.IsFolder));
        }
        Items.RemoveRange(0, count);
        Start += count;
    }

    /// <summary>
    /// A page without what <paramref name="filter"/> hides (<see cref="ListingFilter.Only"/> is not used here).
    /// </summary>
    public RemoteListing PageOf(RemoteListingQuery query, ListingFilter? filter)
    {
        if (filter is null)
        {
            var from = Math.Max(0, query.Offset - Start);
            var items = Items.Skip(from).Take(query.Limit).ToList();
            return new RemoteListing(items, query.Offset, Start + Items.Count, Folders, Sorted, Cursor is not null);
        }

        var hidden = HiddenBy(filter);
        // The item at the offset once the hidden ones are left out: each hidden one up to it moves it one further.
        var raw = query.Offset;
        foreach (var (index, _) in hidden)
        {
            if (index > raw)
                break;
            raw++;
        }

        var page = new List<RemoteItem>(Math.Min(query.Limit, Items.Count));
        for (var i = Math.Max(0, raw - Start); i < Items.Count && page.Count < query.Limit; i++)
        {
            if (!filter.Hidden.Contains(Items[i].Name))
                page.Add(Items[i]);
        }
        return new RemoteListing(
            page,
            query.Offset,
            Start + Items.Count - hidden.Count,
            Folders - hidden.Count(h => h.IsFolder),
            Sorted,
            Cursor is not null
        );
    }

    /// <summary>The places of the items <paramref name="filter"/> hides, in order, those left behind first.</summary>
    private List<(int Index, bool IsFolder)> HiddenBy(ListingFilter filter)
    {
        var hidden = _left.Where(l => filter.Hidden.Contains(l.Name)).Select(l => (l.Index, l.IsFolder)).ToList();
        for (var i = 0; i < Items.Count; i++)
        {
            if (filter.Hidden.Contains(Items[i].Name))
                hidden.Add((Start + i, Items[i].IsFolder));
        }
        return hidden;
    }
}
