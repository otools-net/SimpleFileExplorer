using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using Sfe.Core.Access;
using Sfe.Core.Data;
using Sfe.Core.Indexing;
using Sfe.Core.Paths;
using Sfe.Core.Search;

namespace Sfe.Core.Tagging;

/// <summary>
/// Tags every file of a folder (optionally with its sub-folders), or every match of a search, at once. Unlike
/// <see cref="TagService.ChangeFileTagsAsync"/>, the client doesn't send the files: a folder or a search can hold
/// millions of them, so they are read from the index here, a batch at a time.
/// </summary>
public sealed class ScopeTagger(
    IDbContextFactory<SfeDbContext> dbFactory,
    LibraryPaths paths,
    DirectoryIndexer indexer,
    ScanCoordinator scans,
    TagService tags,
    FileSearchService search)
{
    private const int BatchSize = FileTagRows.BatchSize;

    /// <summary>The indexed files of the scope, and how many of them have each tag.</summary>
    /// <param name="visible">Where the asker may see files (folder rules); null = everywhere.</param>
    /// <returns>null when the scope's folder is not a folder of the library.</returns>
    public async Task<ScopeTags?> GetTagsAsync(TagScope scope, CancellationToken cancellationToken = default, PathFilter? visible = null)
    {
        if (!TryResolve(scope, out var directory))
            return null;

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var graph = await TagGraph.LoadAsync(db, cancellationToken);
        var files = (await FilesAsync(db, graph, scope, directory, cancellationToken)).WhereVisible(visible, f => f.FullName, paths);
        var count = await files.CountAsync(cancellationToken);
        var counts = count == 0 ? [] : (await files
            .SelectMany(f => f.TagLinks)
            .GroupBy(l => l.TagId)
            .Select(g => new { TagId = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken))
            .Select(c => (c.TagId, c.Count))
            .ToList();

        var usages = counts
            .Where(c => graph.Contains(c.TagId))
            .Select(c => new TagUsage(graph.Summary(c.TagId)!, c.Count))
            .OrderByDescending(u => u.FileCount)
            .ThenBy(u => u.Tag.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        return new ScopeTags(Describe(scope, directory), count, usages);
    }

    /// <summary>
    /// Adds and removes tags on every file of the scope. A folder is indexed first (with its sub-folders when
    /// recursive), so that its files missing from the index are tagged too; a search changes the files it matches,
    /// which are indexed ones.
    /// </summary>
    /// <remarks>
    /// The files are changed by batches in order of their index id, each batch read after the previous one is
    /// written: a search whose matches depend on the tags changed (<c>-t:x</c> while adding x) still reaches every
    /// file it matched at the start. A cancelled request keeps the batches already written. With
    /// <paramref name="visible"/> (folder rules), only the files the asker may see are changed.
    /// </remarks>
    public async Task<ScopeTagOutcome> ChangeAsync(ScopeTagChange change, CancellationToken cancellationToken = default, PathFilter? visible = null)
    {
        var add = (change.Add ?? []).Distinct().ToList();
        var remove = (change.Remove ?? []).Distinct().Where(id => !add.Contains(id)).ToList();
        if (add.Count + remove.Count == 0)
            return ScopeTagOutcome.Invalid("add", "No tag to add or remove.");

        var scope = new TagScope(change.Path, change.Recursive, change.Query);
        if (!TryResolve(scope, out var directory))
            return ScopeTagOutcome.NotFound;

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var graph = await TagGraph.LoadAsync(db, cancellationToken);
        foreach (var unknown in add.Concat(remove).Where(id => !graph.Contains(id)))
            return ScopeTagOutcome.Invalid(add.Contains(unknown) ? "add" : "remove", $"Tag {unknown} does not exist.");

        if (!scope.IsSearch)
        {
            // Indexing next to a scan of the same folders would write the same rows twice.
            if (scans.Status.State == ScanState.Running)
                return new ScopeTagOutcome(ScopeTagStatus.ScanRunning);
            await indexer.IndexAsync(directory, scope.Recursive, new ScanProgress(), cancellationToken);
        }

        var files = (await FilesAsync(db, graph, scope, directory, cancellationToken)).WhereVisible(visible, f => f.FullName, paths);
        var count = 0;
        var added = 0;
        var removed = 0;
        var lastId = 0;
        try
        {
            while (true)
            {
                var after = lastId;
                var ids = await files
                    .Where(f => f.Id > after)
                    .OrderBy(f => f.Id)
                    .Select(f => f.Id)
                    .Take(BatchSize)
                    .ToListAsync(cancellationToken);
                if (ids.Count == 0)
                    break;
                lastId = ids[^1];
                count += ids.Count;

                added += await FileTagRows.AddAsync(db, ids, add, cancellationToken);

                if (remove.Count > 0)
                {
                    removed += await db.FileTags
                        .Where(l => ids.Contains(l.FileId) && remove.Contains(l.TagId))
                        .ExecuteDeleteAsync(cancellationToken);
                }
            }
        }
        finally
        {
            if (added + removed > 0)
                tags.InvalidateFileCounts();
        }

        return new ScopeTagOutcome(ScopeTagStatus.Ok, new ScopeTagChangeResult(Describe(scope, directory), count, added, removed));
    }

    /// <summary>The scope's folder, as stored in the index: without trailing separator (drive roots keep theirs).</summary>
    private bool TryResolve(TagScope scope, [NotNullWhen(true)] out string? directory)
    {
        directory = null;
        if (!paths.TryResolve(scope.Path, out var fullPath) || !Directory.Exists(fullPath))
            return false;
        directory = Path.TrimEndingDirectorySeparator(fullPath);
        return true;
    }

    private async Task<IQueryable<FileEntry>> FilesAsync(SfeDbContext db, TagGraph graph, TagScope scope, string directory, CancellationToken cancellationToken)
    {
        if (scope.IsSearch)
            return search.Matches(db, graph, scope.Query, directory);

        var files = db.Files.AsNoTracking();
        if (scope.Recursive)
            return files.Where(SearchPredicateBuilder.InTree<FileEntry>(f => f.FullName, directory));

        var folderId = await db.Directories
            .Where(d => d.FullName == directory)
            .Select(d => (int?)d.Id)
            .FirstOrDefaultAsync(cancellationToken);
        return folderId is { } id ? files.Where(f => f.DirectoryId == id) : files.Where(f => false);
    }

    private TagScope Describe(TagScope scope, string directory) => new(
        paths.ToRelative(directory) ?? string.Empty,
        !scope.IsSearch && scope.Recursive,
        scope.IsSearch ? scope.Query!.Trim() : null);
}
