using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Otools.Net.Sfe.Core.Data;

namespace Otools.Net.Sfe.Core.Connections.Indexing;

/// <summary>
/// Writes a batch of one connection's items (a page of a listing, of the changes, or what SFE just changed) into a
/// library's index, inside the caller's transaction; the caller saves at the end. Files are found by their provider
/// id first, then by their path, so a file renamed or moved keeps its row and its tags. A row standing where another
/// file now is is set aside (<see cref="RemoteIndexKeys.Parked"/>, removed) rather than dropped: its file may be met
/// again elsewhere in the same sync. What is gone is marked removed, and deleted when the sync ends
/// (<see cref="RemoteIndexCleanup"/>). Memory: the rows of the batch only.
/// </summary>
/// <param name="scope">The connection, its case rule, the current pass, and whether new folders are complete (the
/// changes bring their items) or still to list (a full listing).</param>
internal sealed class RemoteIndexWriter(SfeDbContext db, RemoteIndexScope scope)
{
    private readonly Dictionary<string, RemoteDirectory> _folders = new(StringComparer.Ordinal);
    private readonly Dictionary<string, RemoteFile> _byKey = new(StringComparer.Ordinal);
    private readonly Dictionary<string, RemoteFile> _byId = new(StringComparer.Ordinal);

    /// <summary>Paths left out of the index: longer than it stores.</summary>
    public List<string> TooLong { get; } = [];

    /// <summary>Reads the rows the items may meet, in three queries for the whole batch.</summary>
    public async Task LoadAsync(IReadOnlyCollection<RemoteItem> items, CancellationToken cancellationToken)
    {
        var keys = items.Select(i => Key(i.Path)).Distinct().ToList();
        var ids = items.Select(RemoteIndexKeys.IdOf).OfType<string>().Distinct().ToList();
        var folderKeys = keys.Concat(items.Select(i => Key(RemotePaths.Parent(i.Path)))).Distinct().ToList();
        var id = scope.ConnectionId;
        foreach (
            var row in await db
                .RemoteFiles.Where(f => f.ConnectionId == id && keys.Contains(f.FullName))
                .ToListAsync(cancellationToken)
        )
            Know(row);
        if (ids.Count > 0)
        {
            foreach (
                var row in await db
                    .RemoteFiles.Where(f => f.ConnectionId == id && ids.Contains(f.ProviderId!))
                    .ToListAsync(cancellationToken)
            )
                Know(row);
        }
        foreach (
            var folder in await db
                .RemoteDirectories.Where(d => d.ConnectionId == id && folderKeys.Contains(d.FullName))
                .ToListAsync(cancellationToken)
        )
            _folders[folder.FullName] = folder;
    }

    /// <summary>A change of the provider: the item as it is now, or gone.</summary>
    public Task ApplyAsync(RemoteChange change, CancellationToken cancellationToken) =>
        change.Item is { } item ? PutAsync(item, cancellationToken) : RemoveAsync(change.Path, cancellationToken);

    /// <summary>The item as the provider tells it now.</summary>
    public Task PutAsync(RemoteItem item, CancellationToken cancellationToken)
    {
        if (RemoteIndexKeys.IsTooLong(item.Path))
        {
            TooLong.Add(item.Path);
            return Task.CompletedTask;
        }
        return item.IsFolder ? PutFolderAsync(item, cancellationToken) : PutFileAsync(item, cancellationToken);
    }

    /// <summary>Marks an item and everything below it removed (deleted when the sync ends, unless met again).</summary>
    public async Task RemoveAsync(string path, CancellationToken cancellationToken)
    {
        var key = Key(path);
        if (key.Length == 0)
            return;
        // What this batch holds goes first, so that the update below sees it.
        await db.SaveChangesAsync(cancellationToken);
        var id = scope.ConnectionId;
        await db
            .RemoteFiles.InTree(db, id, key)
            .ExecuteUpdateAsync(s => s.SetProperty(f => f.Removed, true), cancellationToken);
        await db
            .RemoteDirectories.InTree(db, id, key)
            .ExecuteUpdateAsync(s => s.SetProperty(d => d.Removed, true), cancellationToken);
        // The rows already read now hold what the database holds: taking one back is then a change to save.
        foreach (var file in _byKey.Values.Where(f => RemoteTrees.IsInTree(f.FullName, key)))
            MarkRemoved(db.Entry(file).Property(f => f.Removed));
        foreach (var folder in _folders.Values.Where(f => RemoteTrees.IsInTree(f.FullName, key)))
            MarkRemoved(db.Entry(folder).Property(f => f.Removed));
    }

    private static void MarkRemoved<T>(PropertyEntry<T, bool> removed)
        where T : class
    {
        removed.CurrentValue = true;
        removed.OriginalValue = true;
    }

    /// <summary>The folder row of a path, made (with those above it) when missing, taken back when removed.</summary>
    public async Task<RemoteDirectory> FolderAsync(string path, CancellationToken cancellationToken)
    {
        var key = Key(path);
        if (!_folders.TryGetValue(key, out var folder))
        {
            var id = scope.ConnectionId;
            folder = await db.RemoteDirectories.FirstOrDefaultAsync(
                d => d.ConnectionId == id && d.FullName == key,
                cancellationToken
            );
            folder ??= await NewFolderAsync(path, key, cancellationToken);
            _folders[key] = folder;
        }
        if (folder.Removed || folder.Pass != scope.Pass)
        {
            folder.Removed = false;
            folder.Pass = scope.Pass;
        }
        return folder;
    }

    private async Task<RemoteDirectory> NewFolderAsync(string path, string key, CancellationToken cancellationToken)
    {
        var parent = key.Length == 0 ? null : await FolderAsync(RemotePaths.Parent(path), cancellationToken);
        var folder = new RemoteDirectory
        {
            ConnectionId = scope.ConnectionId,
            FullName = key,
            DisplayPath = path,
            Name = RemotePaths.NameOf(path),
            Parent = parent,
            Pass = scope.Pass,
            Listed = scope.Following,
        };
        db.RemoteDirectories.Add(folder);
        return folder;
    }

    private async Task PutFolderAsync(RemoteItem item, CancellationToken cancellationToken)
    {
        var key = Key(item.Path);
        var seenBefore = _folders.TryGetValue(key, out var known) && known.Pass == scope.Pass && !known.Removed;
        var folder = await FolderAsync(item.Path, cancellationToken);
        if (key.Length > 0)
            folder.Parent = await FolderAsync(RemotePaths.Parent(item.Path), cancellationToken);
        folder.DisplayPath = item.Path;
        folder.Name = RemotePaths.NameOf(item.Path);
        folder.ProviderId = RemoteIndexKeys.IdOf(item) ?? folder.ProviderId;
        folder.LastWriteTime = item.Modified is { } date ? DateTime.SpecifyKind(date, DateTimeKind.Utc) : null;
        // A full listing goes into each folder it meets once; the changes bring a folder's items with it.
        if (!scope.Following && !seenBefore)
            folder.Pending = true;
        if (scope.Following)
            folder.Listed = true;
        if (_byKey.GetValueOrDefault(key) is { Removed: false } file)
            await ParkAsync(file, cancellationToken);
    }

    private async Task PutFileAsync(RemoteItem item, CancellationToken cancellationToken)
    {
        var key = Key(item.Path);
        var folder = await FolderAsync(RemotePaths.Parent(item.Path), cancellationToken);
        if (_folders.GetValueOrDefault(key) is { Removed: false })
            await RemoveAsync(item.Path, cancellationToken);
        var row = await RowOfAsync(item, key, cancellationToken);
        if (row is null)
        {
            row = new RemoteFile { ConnectionId = scope.ConnectionId, FullName = key };
            db.RemoteFiles.Add(row);
        }
        else if (row.FullName != key)
        {
            await MoveAsync(row, key, cancellationToken);
        }
        RemoteIndexKeys.Describe(row, item);
        row.Directory = folder;
        row.Pass = scope.Pass;
        row.Removed = false;
        Know(row);
    }

    /// <summary>
    /// The row of a file: the one with its id, else the one at its path when nothing tells them apart. A row at its
    /// path with another id is set aside.
    /// </summary>
    private async Task<RemoteFile?> RowOfAsync(RemoteItem item, string key, CancellationToken cancellationToken)
    {
        var id = RemoteIndexKeys.IdOf(item);
        var row = id is null ? null : _byId.GetValueOrDefault(id);
        var occupant = _byKey.GetValueOrDefault(key);
        if (occupant is null || occupant == row)
            return row;
        if (row is null && (occupant.ProviderId is null || id is null || occupant.ProviderId == id))
            return occupant;
        await ParkAsync(occupant, cancellationToken);
        return row;
    }

    /// <summary>A row leaves its path for another: saved at once, so that two rows never hold one path.</summary>
    private async Task MoveAsync(RemoteFile row, string key, CancellationToken cancellationToken)
    {
        if (_byKey.GetValueOrDefault(row.FullName) == row)
            _byKey.Remove(row.FullName);
        row.FullName = key;
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>A row set aside: removed, at a path of its own, until its id is met again.</summary>
    private async Task ParkAsync(RemoteFile row, CancellationToken cancellationToken)
    {
        if (row.Id == 0)
            await db.SaveChangesAsync(cancellationToken);
        if (_byKey.GetValueOrDefault(row.FullName) == row)
            _byKey.Remove(row.FullName);
        row.FullName = RemoteIndexKeys.Parked(row.Id);
        row.Removed = true;
        await db.SaveChangesAsync(cancellationToken);
    }

    private void Know(RemoteFile row)
    {
        _byKey[row.FullName] = row;
        if (row.ProviderId is { } id)
            _byId[id] = row;
    }

    private string Key(string path) => RemoteIndexKeys.Key(path, scope.CaseInsensitive);
}
