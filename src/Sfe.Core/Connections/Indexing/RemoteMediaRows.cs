using Microsoft.EntityFrameworkCore;
using Otools.Net.Sfe.Core.Library;

namespace Otools.Net.Sfe.Core.Connections.Indexing;

/// <summary>
/// The size in pixels and the length of connections' files on their rows in the library's index: written when a
/// thumbnail or the content cache tells them (never by a pass downloading everything), read for listings. Only the
/// row of the same content (provider id and hash or size) is written: a file changed meanwhile keeps nothing.
/// </summary>
public sealed class RemoteMediaRows(RemoteIndexTransactions store)
{
    /// <summary>
    /// The file at <paramref name="path"/> as the library's index holds it (id, hash, size, date: what the cache's key
    /// is made of), without asking the provider; null when the index doesn't hold it.
    /// </summary>
    public async Task<RemoteItem?> FindAsync(RemoteTagPlace place, string path, CancellationToken cancellationToken)
    {
        if (RemoteIndexKeys.IsTooLong(path))
            return null;
        var id = place.ConnectionId;
        var key = place.KeyOf(path);
        await using var db = await store.ContextAsync(cancellationToken);
        if (!await place.IsCurrentAsync(db, cancellationToken))
            return null;
        var row = await db
            .RemoteFiles.AsNoTracking()
            .Where(f => f.ConnectionId == id && f.FullName == key && !f.Removed)
            .Select(f => new IndexListingRow
            {
                FullName = f.FullName,
                DisplayPath = f.DisplayPath,
                Extension = f.Extension,
                Size = f.Size,
                Modified = f.LastWriteTime,
                ProviderId = f.ProviderId,
                Hash = f.ContentHash,
                Downloadable = f.Downloadable,
            })
            .FirstOrDefaultAsync(cancellationToken);
        return row?.ToItem();
    }

    /// <summary>
    /// Writes what <paramref name="media"/> tells on the file's row, when it lacks it; false when none.
    /// </summary>
    public async Task<bool> FillAsync(
        RemoteTagPlace place,
        RemoteItem item,
        RemoteMedia media,
        CancellationToken cancellationToken
    )
    {
        if (!media.IsKnown || RemoteIndexKeys.IsTooLong(item.Path))
            return false;
        var id = place.ConnectionId;
        var key = place.KeyOf(item.Path);
        var hash = RemoteIndexKeys.HashOf(item);
        var size = item.Size ?? 0;
        await using var db = await store.ContextAsync(cancellationToken);
        if (!await place.IsCurrentAsync(db, cancellationToken))
            return false;
        var written = await db
            .RemoteFiles.Where(f =>
                f.ConnectionId == id
                && f.FullName == key
                && !f.Removed
                && f.ContentHash == hash
                && f.Size == size
                && (f.Width == null || f.Duration == null)
            )
            .ExecuteUpdateAsync(
                set =>
                    set.SetProperty(f => f.Width, f => f.Width ?? media.Width)
                        .SetProperty(f => f.Height, f => f.Height ?? media.Height)
                        .SetProperty(f => f.Duration, f => f.Duration ?? media.Duration),
                cancellationToken
            );
        return written > 0;
    }

    /// <summary>
    /// <paramref name="items"/> with the size and length their rows know (one query per page); folders and files
    /// without a row unchanged.
    /// </summary>
    public async Task<IReadOnlyList<LibraryItem>> AttachAsync(
        RemoteTagPlace place,
        IReadOnlyList<LibraryItem> items,
        CancellationToken cancellationToken
    )
    {
        var keys = items
            .Where(i => i.Kind is ItemKind.Image or ItemKind.Video or ItemKind.Audio)
            .Where(i => !RemoteIndexKeys.IsTooLong(i.Path))
            .Select(i => place.KeyOf(i.Path))
            .Distinct()
            .ToList();
        if (keys.Count == 0)
            return items;
        var id = place.ConnectionId;
        await using var db = await store.ContextAsync(cancellationToken);
        if (!await place.IsCurrentAsync(db, cancellationToken))
            return items;
        var known = await db
            .RemoteFiles.AsNoTracking()
            .Where(f => f.ConnectionId == id && keys.Contains(f.FullName) && !f.Removed)
            .Where(f => f.Width != null || f.Duration != null)
            .Select(f => new
            {
                f.FullName,
                f.Width,
                f.Height,
                f.Duration,
            })
            .ToDictionaryAsync(f => f.FullName, StringComparer.Ordinal, cancellationToken);
        if (known.Count == 0)
            return items;
        return
        [
            .. items.Select(item =>
                item.Kind == ItemKind.Directory || !known.TryGetValue(place.KeyOf(item.Path), out var row)
                    ? item
                    : item with
                    {
                        Width = row.Width > 0 ? row.Width : null,
                        Height = row.Height > 0 ? row.Height : null,
                        Duration = row.Duration > 0 ? row.Duration : null,
                    }
            ),
        ];
    }
}
