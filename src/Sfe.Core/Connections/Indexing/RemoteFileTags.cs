using Microsoft.EntityFrameworkCore;
using Otools.Net.Sfe.Core.Data;
using Otools.Net.Sfe.Core.Library;
using Otools.Net.Sfe.Core.Tagging;

namespace Otools.Net.Sfe.Core.Connections.Indexing;

/// <summary>
/// The library's tags on the files of a connection, kept on their rows in the library's index
/// (<see cref="RemoteFileTag"/>): read for listings and the tag dialogs, changed by them and by transfers. A file
/// without a row (not indexed yet, or removed) has no tags. Writes take the connection's lock, as the sync's do, so a
/// row is never tagged while a sync drops it. Access (connection, folder rules, rights) is the caller's to check.
/// </summary>
public sealed class RemoteFileTags(RemoteIndexTransactions store)
{
    /// <summary>The rows of the files at <paramref name="paths"/> the index holds (not removed).</summary>
    public async Task<IReadOnlyList<RemoteTaggedFile>> RowsAsync(
        RemoteTagPlace place,
        IReadOnlyCollection<string> paths,
        CancellationToken cancellationToken
    )
    {
        var keys = paths.Where(p => !RemoteIndexKeys.IsTooLong(p)).Select(place.KeyOf).Distinct().ToList();
        if (keys.Count == 0)
            return [];
        var id = place.ConnectionId;
        await using var db = await store.ContextAsync(cancellationToken);
        if (!await place.IsCurrentAsync(db, cancellationToken))
            return [];
        return await db
            .RemoteFiles.AsNoTracking()
            .Where(f => f.ConnectionId == id && keys.Contains(f.FullName) && !f.Removed)
            .Select(f => new RemoteTaggedFile(f.Id, f.DisplayPath, f.ProviderId))
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// <paramref name="items"/> with the tags of their files (folders unchanged): one query per page.
    /// </summary>
    public async Task<IReadOnlyList<LibraryItem>> AttachAsync(
        RemoteTagPlace place,
        IReadOnlyList<LibraryItem> items,
        CancellationToken cancellationToken
    )
    {
        var files = items.Where(i => i.Kind != ItemKind.Directory).Select(i => i.Path).ToList();
        if (files.Count == 0)
            return items;
        var keys = files.Where(p => !RemoteIndexKeys.IsTooLong(p)).Select(place.KeyOf).Distinct().ToList();
        var id = place.ConnectionId;
        await using var db = await store.ContextAsync(cancellationToken);
        if (!await place.IsCurrentAsync(db, cancellationToken))
            return items;
        var links = await db
            .RemoteFileTags.Where(l => l.File!.ConnectionId == id && keys.Contains(l.File.FullName) && !l.File.Removed)
            .Select(l => new { l.File!.FullName, l.TagId })
            .ToListAsync(cancellationToken);
        if (links.Count == 0)
            return items;
        var graph = await TagGraph.LoadAsync(db, cancellationToken);
        var byKey = links.ToLookup(l => l.FullName, l => l.TagId, StringComparer.Ordinal);
        return
        [
            .. items.Select(item =>
                item.Kind == ItemKind.Directory || !byKey.Contains(place.KeyOf(item.Path))
                    ? item
                    : item with
                    {
                        Tags = TagService.Summaries(graph, byKey[place.KeyOf(item.Path)]),
                    }
            ),
        ];
    }

    /// <summary>The tags of the file at <paramref name="path"/>; none when the index doesn't hold it.</summary>
    public async Task<FileTags> TagsOfAsync(RemoteTagPlace place, string path, CancellationToken cancellationToken)
    {
        var rows = await RowsAsync(place, [path], cancellationToken);
        if (rows.Count == 0)
            return new FileTags(path, [], []);
        await using var db = await store.ContextAsync(cancellationToken);
        var graph = await TagGraph.LoadAsync(db, cancellationToken);
        var ids = await TagIdsAsync(db, [rows[0].Id], cancellationToken);
        return ToFileTags(graph, rows[0].Path, ids[rows[0].Id]);
    }

    /// <summary>Adds and removes tags on the files of <paramref name="rows"/> (at most a batch of them).</summary>
    /// <returns>The files' tags after the change; or, nothing changed, the id of a tag that doesn't exist.</returns>
    public async Task<(FileTagChangeResult? Result, int? UnknownTag)> ChangeAsync(
        RemoteTagPlace place,
        IReadOnlyList<RemoteTaggedFile> rows,
        (IReadOnlyList<int> Add, IReadOnlyList<int> Remove) change,
        CancellationToken cancellationToken
    )
    {
        await using var db = await store.ContextAsync(cancellationToken);
        var graph = await TagGraph.LoadAsync(db, cancellationToken);
        var tags = change.Add.Concat(change.Remove);
        if (tags.Where(t => !graph.Contains(t)).Select(t => (int?)t).FirstOrDefault() is { } missing)
            return (null, missing);
        var ids = rows.Select(r => r.Id).Distinct().ToList();
        var (added, removed) = (0, 0);
        await store.WriteAsync(
            place.ConnectionId,
            async (write, token) =>
            {
                added = await RemoteFileTagRows.AddAsync(write, ids, change.Add, token);
                removed = await RemoteFileTagRows.RemoveAsync(write, ids, change.Remove, token);
            },
            cancellationToken
        );
        var links = await TagIdsAsync(db, ids, cancellationToken);
        var files = rows.DistinctBy(r => r.Id).Select(r => ToFileTags(graph, r.Path, links[r.Id])).ToList();
        return (new FileTagChangeResult(added, removed, files), null);
    }

    /// <summary>The tags of the file at <paramref name="path"/> (a transfer's source); none without a row.</summary>
    public async Task<IReadOnlyList<int>> TagIdsAsync(
        RemoteTagPlace place,
        string path,
        CancellationToken cancellationToken
    )
    {
        var rows = await RowsAsync(place, [path], cancellationToken);
        if (rows.Count == 0)
            return [];
        await using var db = await store.ContextAsync(cancellationToken);
        return [.. (await TagIdsAsync(db, [rows[0].Id], cancellationToken))[rows[0].Id]];
    }

    /// <summary>Gives tags to the file at <paramref name="path"/> (a transfer's target).</summary>
    /// <returns>False when the index has no row of it: nothing was tagged.</returns>
    public async Task<bool> AddAsync(
        RemoteTagPlace place,
        string path,
        IReadOnlyList<int> tagIds,
        CancellationToken cancellationToken
    )
    {
        var rows = await RowsAsync(place, [path], cancellationToken);
        if (rows.Count == 0)
            return false;
        await store.WriteAsync(
            place.ConnectionId,
            async (db, token) =>
            {
                var known = await db.Tags.Where(t => tagIds.Contains(t.Id)).Select(t => t.Id).ToListAsync(token);
                await RemoteFileTagRows.AddAsync(db, [rows[0].Id], known, token);
            },
            cancellationToken
        );
        return true;
    }

    /// <summary>
    /// Takes every tag off the file at <paramref name="path"/> (a moved source), its row marked removed or not.
    /// </summary>
    public async Task ForgetAsync(RemoteTagPlace place, string path, CancellationToken cancellationToken)
    {
        if (RemoteIndexKeys.IsTooLong(path))
            return;
        var (id, key) = (place.ConnectionId, place.KeyOf(path));
        await store.WriteAsync(
            id,
            (db, token) =>
                db
                    .RemoteFileTags.Where(l => l.File!.ConnectionId == id && l.File.FullName == key)
                    .ExecuteDeleteAsync(token),
            cancellationToken
        );
    }

    private static async Task<ILookup<long, int>> TagIdsAsync(
        SfeDbContext db,
        IReadOnlyCollection<long> fileIds,
        CancellationToken cancellationToken
    ) =>
        (
            await db
                .RemoteFileTags.Where(l => fileIds.Contains(l.FileId))
                .Select(l => new { l.FileId, l.TagId })
                .ToListAsync(cancellationToken)
        ).ToLookup(l => l.FileId, l => l.TagId);

    private static FileTags ToFileTags(TagGraph graph, string path, IEnumerable<int> tagIds)
    {
        var ids = tagIds.Distinct().ToList();
        return new FileTags(path, TagService.Summaries(graph, ids), graph.GroupByCategory(ids));
    }
}
