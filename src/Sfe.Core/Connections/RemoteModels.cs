namespace Otools.Net.Sfe.Core.Connections;

/// <param name="Cursor">Asks for the next page; null on the last one.</param>
public sealed record RemotePage(IReadOnlyList<RemoteItem> Items, string? Cursor);

/// <summary>
/// Bytes <paramref name="From"/> to <paramref name="To"/> of a file, both included; no end = to its end.
/// </summary>
public sealed record RemoteRange(long From, long? To = null);

/// <summary>Where a finished upload goes.</summary>
/// <param name="Length">The size of the whole file: the sum of the parts sent.</param>
/// <param name="Modified">The file's date on the uploader's computer, kept when the provider takes one.</param>
public sealed record RemoteUploadTarget(
    string Path,
    long Length,
    RemoteConflict Conflict,
    DateTimeOffset? Modified = null
);

/// <summary>What to do when something of the same name is already at the destination.</summary>
public enum RemoteConflict
{
    /// <summary>Nothing is written: <see cref="RemoteErrorKind.Exists"/>.</summary>
    Fail,

    /// <summary>The new item gets a free name next to the other (the provider's own numbering).</summary>
    KeepBoth,

    /// <summary>A file takes the place of the file there; never of a folder.</summary>
    Replace,
}

/// <param name="Item">The item as it is now; null when it was deleted.</param>
public sealed record RemoteChange(string Path, RemoteItem? Item);

/// <param name="Cursor">To ask from next time.</param>
/// <param name="HasMore">More changes wait: ask again with <paramref name="Cursor"/>.</param>
/// <param name="Reset">The provider no longer knows the cursor given: everything must be listed again.</param>
public sealed record RemoteChanges(
    IReadOnlyList<RemoteChange> Changes,
    string Cursor,
    bool HasMore = false,
    bool Reset = false
);

/// <param name="Total">Null when the storage has no limit it tells.</param>
public sealed record RemoteSpace(long Used, long? Total);
