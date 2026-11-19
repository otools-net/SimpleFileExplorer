using Microsoft.EntityFrameworkCore;
using Otools.Net.Sfe.Core.Data;
using Otools.Net.Sfe.Core.Tagging;

namespace Otools.Net.Sfe.Core.Connections.Indexing;

/// <summary>
/// Tags every file of a connection's folder (optionally with its sub-folders), or every match of a search of it, at
/// once, as <see cref="ScopeTagger"/> does in the library; the files are the rows the library's index holds (a file
/// not indexed yet is neither counted nor changed: the provider is never listed for it). Folder rules are conditions
/// of the queries, so hidden files are in no count and never changed; a change touches only the files where the asker
/// may tag. Rows are changed by batches in the order of their ids, each under the connection's lock.
/// </summary>
public sealed class RemoteScopeTagger(RemoteIndexTransactions store, RemoteIndexSearch search)
{
    /// <summary>The scope's files the asker sees, and how many of them have each tag.</summary>
    public async Task<ScopeTags> GetTagsAsync(RemoteTagScope scope, CancellationToken cancellationToken)
    {
        await using var db = await store.ContextAsync(cancellationToken);
        var graph = await TagGraph.LoadAsync(db, cancellationToken);
        var files = await FilesAsync(db, scope, cancellationToken);
        var count = await files.CountAsync(cancellationToken);
        var counts =
            count == 0
                ? []
                : await files
                    .SelectMany(f => f.TagLinks)
                    .GroupBy(l => l.TagId)
                    .Select(g => new { TagId = g.Key, Count = g.Count() })
                    .ToListAsync(cancellationToken);
        var usages = counts
            .Where(c => graph.Contains(c.TagId))
            .Select(c => new TagUsage(graph.Summary(c.TagId)!, c.Count))
            .OrderByDescending(u => u.FileCount)
            .ThenBy(u => u.Tag.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        return new ScopeTags(Describe(scope.Scope), count, usages);
    }

    /// <summary>Adds and removes tags on every file of the scope where the asker may tag.</summary>
    /// <remarks>A cancelled request keeps the batches already written.</remarks>
    public async Task<ScopeTagOutcome> ChangeAsync(
        RemoteTagScope scope,
        IReadOnlyList<int>? add,
        IReadOnlyList<int>? remove,
        CancellationToken cancellationToken
    )
    {
        var adding = (add ?? []).Distinct().ToList();
        var removing = (remove ?? []).Distinct().Where(id => !adding.Contains(id)).ToList();
        if (adding.Count + removing.Count == 0)
            return ScopeTagOutcome.Invalid("add", "No tag to add or remove.");
        await using var db = await store.ContextAsync(cancellationToken);
        var graph = await TagGraph.LoadAsync(db, cancellationToken);
        foreach (var unknown in adding.Concat(removing).Where(id => !graph.Contains(id)))
        {
            return ScopeTagOutcome.Invalid(
                adding.Contains(unknown) ? "add" : "remove",
                $"Tag {unknown} does not exist."
            );
        }

        var files = await FilesAsync(db, scope, cancellationToken);
        if (scope.Taggable is { IsEverything: false } taggable)
            files = files.Where(KeysOf(db, scope).Visible(taggable));
        var tally = await ChangeAllAsync(scope.Target.ConnectionId, files, (adding, removing), cancellationToken);
        return new ScopeTagOutcome(
            ScopeTagStatus.Ok,
            new ScopeTagChangeResult(Describe(scope.Scope), tally.Files, tally.Added, tally.Removed)
        );
    }

    private async Task<(int Files, int Added, int Removed)> ChangeAllAsync(
        int connectionId,
        IQueryable<RemoteFile> files,
        (List<int> Add, List<int> Remove) change,
        CancellationToken cancellationToken
    )
    {
        var (count, added, removed) = (0, 0, 0);
        var lastId = 0L;
        while (true)
        {
            var after = lastId;
            var ids = await files
                .Where(f => f.Id > after)
                .OrderBy(f => f.Id)
                .Select(f => f.Id)
                .Take(RemoteFileTagRows.BatchSize)
                .ToListAsync(cancellationToken);
            if (ids.Count == 0)
                return (count, added, removed);
            (lastId, count) = (ids[^1], count + ids.Count);
            await store.WriteAsync(
                connectionId,
                async (db, token) =>
                {
                    added += await RemoteFileTagRows.AddAsync(db, ids, change.Add, token);
                    removed += await RemoteFileTagRows.RemoveAsync(db, ids, change.Remove, token);
                },
                cancellationToken
            );
        }
    }

    /// <summary>The rows of the scope the asker sees: a search's matches, or a folder's files.</summary>
    private async Task<IQueryable<RemoteFile>> FilesAsync(
        SfeDbContext db,
        RemoteTagScope scope,
        CancellationToken cancellationToken
    )
    {
        if (!await RemoteTagPlace.Of(scope.Target).IsCurrentAsync(db, cancellationToken))
            return db.RemoteFiles.Where(f => false);
        if (scope.Scope.IsSearch)
        {
            var ask = new RemoteSearchAsk
            {
                Target = scope.Target,
                Query = scope.Scope.Query,
                Folder = scope.Folder,
                Visible = scope.Visible,
                HiddenIds = scope.HiddenIds,
            };
            return await search.MatchesAsync(db, ask, cancellationToken);
        }
        var files = await FolderFilesAsync(db, scope, cancellationToken);
        var keys = KeysOf(db, scope);
        if (scope.Visible is { IsEverything: false } visible)
            files = files.Where(keys.Visible(visible));
        return await keys.WithoutHiddenAsync(db, scope.Target.ConnectionId, files, scope.HiddenIds, cancellationToken);
    }

    private static async Task<IQueryable<RemoteFile>> FolderFilesAsync(
        SfeDbContext db,
        RemoteTagScope scope,
        CancellationToken cancellationToken
    )
    {
        var id = scope.Target.ConnectionId;
        var files = db.RemoteFiles.AsNoTracking().Where(f => f.ConnectionId == id && !f.Removed);
        if (scope.Scope.Recursive)
            return files.Where(KeysOf(db, scope).Below(scope.Folder));
        var key = RemoteIndexKeys.Key(scope.Folder, scope.Target.CaseInsensitive);
        var folderId = await db
            .RemoteDirectories.Where(d => d.ConnectionId == id && d.FullName == key && !d.Removed)
            .Select(d => (long?)d.Id)
            .FirstOrDefaultAsync(cancellationToken);
        return folderId is { } folder ? files.Where(f => f.DirectoryId == folder) : files.Where(f => false);
    }

    private static RemoteSearchScope KeysOf(SfeDbContext db, RemoteTagScope scope) =>
        new(scope.Target.CaseInsensitive, db.Database.IsSqlite());

    private static TagScope Describe(TagScope scope) =>
        new(
            scope.Path ?? string.Empty,
            !scope.IsSearch && scope.Recursive,
            scope.IsSearch ? scope.Query!.Trim() : null
        );
}
