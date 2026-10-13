using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Sfe.Core.Data;
using Sfe.Core.Indexing;
using Sfe.Core.Library;
using Sfe.Core.Options;
using Sfe.Core.Paths;

namespace Sfe.Core.Tagging;

/// <summary>
/// Tags and file tagging, following TagStudio's library operations (<c>search_tags</c>, <c>add_tag</c>,
/// <c>update_tag</c>, <c>remove_tag</c>, <c>add_tags_to_entries</c>...). Tags are attached to indexed files: tagging a
/// file that is not indexed yet indexes its folder first.
/// </summary>
public sealed class TagService(IDbContextFactory<SfeDbContext> dbFactory, LibraryPaths paths, DirectoryIndexer indexer, IOptionsMonitor<SfeOptions> options)
{
    public const int MaxFilesPerChange = 1000;

    /* ---------- Tags ---------- */

    /// <summary>
    /// Tags whose name, shorthand or alias contains <paramref name="text"/>. As in TagStudio, names starting with
    /// the text come first (shortest first), and tags inheriting from a match are listed separately.
    /// </summary>
    public async Task<TagSearchResults> SearchAsync(string? text, int limit, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var graph = await TagGraph.LoadAsync(db, cancellationToken);
        var counts = await FileCountsAsync(db, cancellationToken);
        text = text?.Trim() ?? string.Empty;

        if (text.Length == 0)
        {
            var all = graph.Nodes
                .OrderBy(n => n.Name, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(n => n.Id)
                .Take(limit)
                .Select(n => Details(graph, n, counts))
                .ToList();
            return new TagSearchResults(all, []);
        }

        var matches = graph.Nodes
            .Select(node => (node, key: TagGraph.Names(node)
                .Where(name => name.Contains(text, StringComparison.CurrentCultureIgnoreCase))
                .Select(name => MatchKey(name, text))
                .DefaultIfEmpty()
                .Min()))
            .Where(m => m.key.Name is not null)
            .OrderBy(m => m.key)
            .ThenBy(m => m.node.Id)
            .Select(m => m.node)
            .Take(limit)
            .ToList();

        var matchIds = matches.Select(m => m.Id).ToHashSet();
        var descendants = new HashSet<int>();
        foreach (var match in matches)
        {
            if (matchIds.Count + descendants.Count >= limit)
                break;
            descendants.UnionWith(graph.SelfAndDescendants(match.Id).Where(id => !matchIds.Contains(id)));
        }

        return new TagSearchResults(
            matches.Select(n => Details(graph, n, counts)).ToList(),
            descendants
                .Select(id => graph.Find(id)!)
                .OrderBy(n => MatchKey(n.Name, text))
                .Take(Math.Max(0, limit - matchIds.Count))
                .Select(n => Details(graph, n, counts))
                .ToList());
    }

    /// <summary>Sort key of a name containing the search text: prefix matches first, shortest first.</summary>
    private static (int Rank, int Length, string? Name) MatchKey(string name, string text) =>
        name.StartsWith(text, StringComparison.CurrentCultureIgnoreCase)
            ? (0, name.Length, name.ToLowerInvariant())
            : (1, 0, name.ToLowerInvariant());

    public async Task<TagDetails?> GetAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var graph = await TagGraph.LoadAsync(db, cancellationToken);
        return graph.Find(id) is { } node ? Details(graph, node, await FileCountsAsync(db, cancellationToken)) : null;
    }

    public async Task<TagEditResult> CreateAsync(TagInput input, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var graph = await TagGraph.LoadAsync(db, cancellationToken);
        if (Validate(graph, input, id: null) is { } invalid)
            return invalid;

        var tag = new Tag();
        Apply(db, tag, input);
        db.Tags.Add(tag);
        await db.SaveChangesAsync(cancellationToken);

        return new TagEditResult(TagEditStatus.Ok, await GetAsync(tag.Id, cancellationToken));
    }

    public async Task<TagEditResult> UpdateAsync(int id, TagInput input, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var tag = await db.Tags
            .Include(t => t.Aliases)
            .Include(t => t.ParentLinks)
            // One tag with a handful of aliases and parents: the joined rows are few, so a single round trip
            // beats split queries. Stating it explicitly silences EF's MultipleCollectionIncludeWarning.
            .AsSingleQuery()
            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (tag is null)
            return TagEditResult.NotFound;

        var graph = await TagGraph.LoadAsync(db, cancellationToken);
        if (Validate(graph, input, id) is { } invalid)
            return invalid;

        Apply(db, tag, input);
        await db.SaveChangesAsync(cancellationToken);

        return new TagEditResult(TagEditStatus.Ok, await GetAsync(id, cancellationToken));
    }

    /// <summary>Deletes a tag, its aliases, its links to other tags and to files. Built-in tags cannot be deleted.</summary>
    public async Task<TagDeleteStatus> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        if (BuiltInTags.IsBuiltIn(id))
            return TagDeleteStatus.BuiltIn;

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        if (!await db.Tags.AnyAsync(t => t.Id == id, cancellationToken))
            return TagDeleteStatus.NotFound;

        // Same steps as TagStudio's remove_tag; the links to other tags have no cascade (see SfeDbContext).
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            await db.TagParents.Where(p => p.ParentId == id || p.ChildId == id).ExecuteDeleteAsync(cancellationToken);
            await db.TagActivity.Where(a => a.TagId == id).ExecuteDeleteAsync(cancellationToken);
            await db.Tags.Where(t => t.DisambiguationId == id)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.DisambiguationId, (int?)null), cancellationToken);
            await db.TagAliases.Where(a => a.TagId == id).ExecuteDeleteAsync(cancellationToken);
            await db.FileTags.Where(l => l.TagId == id).ExecuteDeleteAsync(cancellationToken);
            await db.MissingFileTags.Where(l => l.TagId == id).ExecuteDeleteAsync(cancellationToken);
            await db.Tags.Where(t => t.Id == id).ExecuteDeleteAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        });

        InvalidateFileCounts();
        return TagDeleteStatus.Deleted;
    }

    /// <returns>null when <paramref name="input"/> is valid.</returns>
    private static TagEditResult? Validate(TagGraph graph, TagInput input, int? id)
    {
        var name = input.Name?.Trim();
        if (string.IsNullOrEmpty(name))
            return TagEditResult.Invalid("name", "A tag needs a name.");
        if (name.Length > SfeDbContext.MaxTagNameLength)
            return TagEditResult.Invalid("name", $"Tag names are limited to {SfeDbContext.MaxTagNameLength} characters.");
        if (input.Shorthand?.Trim().Length > SfeDbContext.MaxTagNameLength)
            return TagEditResult.Invalid("shorthand", $"Shorthands are limited to {SfeDbContext.MaxTagNameLength} characters.");
        if (input.Aliases?.Any(a => a?.Trim().Length > SfeDbContext.MaxTagNameLength) == true)
            return TagEditResult.Invalid("aliases", $"Aliases are limited to {SfeDbContext.MaxTagNameLength} characters.");
        if (!string.IsNullOrWhiteSpace(input.Color) && !TagColors.IsDefined(input.Color))
            return TagEditResult.Invalid("color", $"Unknown color '{input.Color}'.");

        var parents = input.ParentIds ?? [];
        foreach (var parent in parents.Where(p => !graph.Contains(p)))
            return TagEditResult.Invalid("parentIds", $"Parent tag {parent} does not exist.");

        if (id is { } self)
        {
            // A parent that already inherits from this tag would make the hierarchy loop.
            var descendants = graph.SelfAndDescendants(self);
            foreach (var loop in parents.Where(descendants.Contains))
            {
                return TagEditResult.Invalid("parentIds", loop == self
                    ? "A tag cannot be its own parent."
                    : $"“{graph.DisplayName(graph.Find(loop)!)}” already inherits from this tag, so it cannot also be its parent.");
            }
        }

        return null;
    }

    /// <summary>Copies <paramref name="input"/> to a tracked tag, trimming names and dropping duplicate aliases.</summary>
    private static void Apply(SfeDbContext db, Tag tag, TagInput input)
    {
        tag.Name = input.Name.Trim();
        tag.Shorthand = string.IsNullOrWhiteSpace(input.Shorthand) ? null : input.Shorthand.Trim();
        tag.Color = string.IsNullOrWhiteSpace(input.Color) ? null : input.Color;
        tag.IsCategory = input.IsCategory;
        tag.IsHidden = input.IsHidden;

        var parentIds = (input.ParentIds ?? []).Distinct().ToList();
        // As in TagStudio, the disambiguation parent is dropped when it is no longer a parent.
        tag.DisambiguationId = input.DisambiguationId is { } d && parentIds.Contains(d) ? d : null;

        // Links are removed explicitly: EF Core only deletes orphans of cascading relationships.
        foreach (var link in tag.ParentLinks.Where(l => !parentIds.Contains(l.ParentId)).ToList())
        {
            tag.ParentLinks.Remove(link);
            db.TagParents.Remove(link);
        }

        foreach (var parentId in parentIds.Where(p => tag.ParentLinks.All(l => l.ParentId != p)))
            tag.ParentLinks.Add(new TagParent { ParentId = parentId });

        var aliases = (input.Aliases ?? [])
            .Select(a => a?.Trim())
            .OfType<string>()
            .Where(a => a.Length > 0)
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        foreach (var alias in tag.Aliases.Where(a => !aliases.Contains(a.Name)).ToList())
        {
            tag.Aliases.Remove(alias);
            db.TagAliases.Remove(alias);
        }

        foreach (var alias in aliases.Where(a => tag.Aliases.All(existing => existing.Name != a)))
            tag.Aliases.Add(new TagAlias { Name = alias });
    }

    private static TagDetails Details(TagGraph graph, TagGraph.Node node, IReadOnlyDictionary<int, int> counts) => new(
        node.Id,
        node.Name,
        node.Shorthand,
        graph.DisplayName(node),
        node.Color,
        node.IsCategory,
        node.IsHidden,
        BuiltInTags.IsBuiltIn(node.Id),
        node.DisambiguationId,
        node.Aliases.Order(StringComparer.CurrentCultureIgnoreCase).ToList(),
        TagGraph.Sort(node.Parents.Select(graph.Summary).OfType<TagSummary>()),
        counts.GetValueOrDefault(node.Id));

    /// <summary>How long file counts per tag are reused: removing files from the index (scan) changes them too.</summary>
    private static readonly TimeSpan FileCountLifetime = TimeSpan.FromSeconds(30);

    private sealed record FileCounts(Dictionary<int, int> Counts, long Version, DateTime LoadedAt);

    private FileCounts? _fileCounts;
    private long _fileCountsVersion;

    /// <summary>
    /// Number of files per tag. Counting reads every file-tag link, and the tag picker asks for counts as the user
    /// types, so they are kept a short while and dropped when this service changes links.
    /// </summary>
    private async Task<Dictionary<int, int>> FileCountsAsync(SfeDbContext db, CancellationToken cancellationToken)
    {
        var version = Interlocked.Read(ref _fileCountsVersion);
        if (_fileCounts is { } cached && cached.Version == version && DateTime.UtcNow - cached.LoadedAt < FileCountLifetime)
            return cached.Counts;

        var counts = await db.FileTags
            .GroupBy(l => l.TagId)
            .Select(g => new { TagId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.TagId, g => g.Count, cancellationToken);
        _fileCounts = new FileCounts(counts, version, DateTime.UtcNow);
        return counts;
    }

    /// <summary>Drops the cached counts: file-tag links changed (also used by relinking).</summary>
    public void InvalidateFileCounts() => Interlocked.Increment(ref _fileCountsVersion);

    /* ---------- Sidebar ---------- */

    private TagOptions Settings => options.CurrentValue.Tags;

    /// <summary>
    /// Most used tags, last searched tags, tags of the last viewed files, and random tags in none of those lists,
    /// <see cref="TagOptions.SidebarCount"/> of each. The history lists stay empty while history is off.
    /// </summary>
    public async Task<TagSidebar> GetSidebarAsync(CancellationToken cancellationToken = default)
    {
        var count = Math.Max(1, Settings.SidebarCount);
        var history = Settings.RecordHistory;
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var graph = await TagGraph.LoadAsync(db, cancellationToken);
        var counts = await FileCountsAsync(db, cancellationToken);

        TagUsage Usage(int id) => new(graph.Summary(id)!, counts.GetValueOrDefault(id));

        var popular = counts
            .Where(c => c.Value > 0 && graph.Contains(c.Key))
            .OrderByDescending(c => c.Value)
            .ThenBy(c => graph.Find(c.Key)!.Name, StringComparer.CurrentCultureIgnoreCase)
            .Take(count)
            .Select(c => c.Key)
            .ToList();
        var searched = !history ? [] : await db.TagActivity
            .Where(a => a.LastSearchedAt != null)
            .OrderByDescending(a => a.LastSearchedAt)
            .Select(a => a.TagId)
            .Take(count)
            .ToListAsync(cancellationToken);
        var viewed = !history ? [] : await db.TagActivity
            .Where(a => a.LastViewedAt != null)
            .OrderByDescending(a => a.LastViewedAt)
            .Select(a => a.TagId)
            .Take(count)
            .ToListAsync(cancellationToken);

        var shown = popular.Concat(searched).Concat(viewed).ToHashSet();
        var lucky = graph.Nodes
            .Where(n => !shown.Contains(n.Id))
            .Select(n => n.Id)
            .OrderBy(_ => Random.Shared.Next())
            .Take(count)
            .ToList();

        return new TagSidebar(
            popular.Select(Usage).ToList(),
            searched.Where(graph.Contains).Select(Usage).ToList(),
            viewed.Where(graph.Contains).Select(Usage).ToList(),
            lucky.Select(Usage).ToList(),
            history);
    }

    /// <summary>Forgets the searched and viewed tags.</summary>
    /// <returns>The number of tags that had history.</returns>
    public async Task<int> ClearHistoryAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.TagActivity.ExecuteDeleteAsync(cancellationToken);
    }

    /// <summary>Records that a search named these tags.</summary>
    public Task RecordSearchedAsync(IReadOnlyCollection<int> tagIds, CancellationToken cancellationToken = default) =>
        RecordActivityAsync(tagIds, (activity, now) => activity.LastSearchedAt = now, cancellationToken);

    /// <summary>Records that a file was viewed: its tags become the last viewed ones.</summary>
    /// <returns>false when the path is not an existing file of the library.</returns>
    public async Task<bool> RecordViewedAsync(string? relativePath, CancellationToken cancellationToken = default)
    {
        if (!TryResolveFile(relativePath, out var fullPath))
            return false;

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var tagIds = await db.FileTags
            .Where(l => l.File!.FullName == fullPath)
            .Select(l => l.TagId)
            .ToListAsync(cancellationToken);
        await RecordActivityAsync(tagIds, (activity, now) => activity.LastViewedAt = now, cancellationToken);
        return true;
    }

    private async Task RecordActivityAsync(IReadOnlyCollection<int> tagIds, Action<TagActivity, DateTime> update, CancellationToken cancellationToken)
    {
        if (tagIds.Count == 0 || !Settings.RecordHistory)
            return;

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var ids = tagIds.Distinct().ToList();
        var existing = await db.TagActivity.Where(a => ids.Contains(a.TagId)).ToDictionaryAsync(a => a.TagId, cancellationToken);
        var known = await db.Tags.Where(t => ids.Contains(t.Id)).Select(t => t.Id).ToListAsync(cancellationToken);
        var now = DateTime.UtcNow;
        foreach (var id in known)
        {
            if (!existing.TryGetValue(id, out var activity))
                db.TagActivity.Add(activity = new TagActivity { TagId = id });
            update(activity, now);
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Two requests recorded the same new tag at once: one of them wins, which is all a "last used" needs.
        }
    }

    /* ---------- File tags ---------- */

    /// <returns>null when the path is not an existing file of the library.</returns>
    public async Task<FileTags?> GetFileTagsAsync(string? relativePath, CancellationToken cancellationToken = default)
    {
        if (!TryResolveFile(relativePath, out var fullPath))
            return null;

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var tagIds = await db.FileTags
            .Where(l => l.File!.FullName == fullPath)
            .Select(l => l.TagId)
            .ToListAsync(cancellationToken);
        var graph = await TagGraph.LoadAsync(db, cancellationToken);
        return ToFileTags(graph, paths.ToRelative(fullPath)!, tagIds);
    }

    /// <summary>Adds and removes tags on files (TagStudio's add_tags_to_entries/remove_tags_from_entries).</summary>
    public async Task<(FileTagChangeResult? Result, IDictionary<string, string[]>? Errors)> ChangeFileTagsAsync(
        FileTagChange change, CancellationToken cancellationToken = default)
    {
        var add = (change.Add ?? []).Distinct().ToList();
        var remove = (change.Remove ?? []).Distinct().Where(id => !add.Contains(id)).ToList();
        var requested = change.Paths ?? [];
        if (requested.Count == 0)
            return (null, Error("paths", "No file given."));
        if (requested.Count > MaxFilesPerChange)
            return (null, Error("paths", $"At most {MaxFilesPerChange} files can be tagged at once."));

        var files = new List<string>();
        foreach (var path in requested)
        {
            if (!TryResolveFile(path, out var fullPath))
                return (null, Error("paths", $"“{path}” is not a file of the library."));
            files.Add(fullPath);
        }

        files = files.Distinct(LibraryPaths.PathComparer).ToList();

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var graph = await TagGraph.LoadAsync(db, cancellationToken);
        foreach (var unknown in add.Concat(remove).Where(id => !graph.Contains(id)))
            return (null, Error(add.Contains(unknown) ? "add" : "remove", $"Tag {unknown} does not exist."));

        var fileIds = await indexer.IndexFilesAsync(files, cancellationToken);
        if (fileIds.Count < files.Count)
        {
            var missing = files.First(f => !fileIds.ContainsKey(f));
            return (null, Error("paths", $"“{paths.ToRelative(missing)}” cannot be indexed (ignored, or its path is too long), so it cannot be tagged."));
        }

        var ids = fileIds.Values.ToList();
        var added = 0;
        var removed = 0;
        if (add.Count > 0)
        {
            var existing = (await db.FileTags
                    .Where(l => ids.Contains(l.FileId) && add.Contains(l.TagId))
                    .Select(l => new { l.FileId, l.TagId })
                    .ToListAsync(cancellationToken))
                .Select(l => (l.FileId, l.TagId))
                .ToHashSet();
            foreach (var fileId in ids)
            {
                foreach (var tagId in add.Where(t => !existing.Contains((fileId, t))))
                {
                    db.FileTags.Add(new FileTag { FileId = fileId, TagId = tagId });
                    added++;
                }
            }

            await db.SaveChangesAsync(cancellationToken);
        }

        if (remove.Count > 0)
        {
            removed = await db.FileTags
                .Where(l => ids.Contains(l.FileId) && remove.Contains(l.TagId))
                .ExecuteDeleteAsync(cancellationToken);
        }

        if (added + removed > 0)
            InvalidateFileCounts();

        var links = (await db.FileTags
                .Where(l => ids.Contains(l.FileId))
                .Select(l => new { l.FileId, l.TagId })
                .ToListAsync(cancellationToken))
            .ToLookup(l => l.FileId, l => l.TagId);
        var results = files
            .Select(f => ToFileTags(graph, paths.ToRelative(f)!, links[fileIds[f]]))
            .ToList();

        return (new FileTagChangeResult(added, removed, results), null);
    }

    /// <summary>
    /// Fills what the index knows of the files among <paramref name="items"/>: their <see cref="LibraryItem.Tags"/>,
    /// the resolution of images and videos and the duration of videos and audio files; and for files and folders
    /// alike, whether the index has them (<see cref="LibraryItem.Indexed"/>).
    /// </summary>
    public async Task<IReadOnlyList<LibraryItem>> AttachIndexDataAsync(IReadOnlyList<LibraryItem> items, CancellationToken cancellationToken = default)
    {
        var byFullName = new Dictionary<string, int>(LibraryPaths.PathComparer);
        var folderByFullName = new Dictionary<string, int>(LibraryPaths.PathComparer);
        for (var i = 0; i < items.Count; i++)
        {
            if (paths.TryResolve(items[i].Path, out var fullPath))
                (items[i].Kind == ItemKind.Directory ? folderByFullName : byFullName)[fullPath] = i;
        }

        if (byFullName.Count == 0 && folderByFullName.Count == 0)
            return items;

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var result = items.Select(i => i.Kind == ItemKind.Directory ? i with { Indexed = false } : i with { Tags = [], Indexed = false }).ToList();

        if (folderByFullName.Count > 0)
        {
            var folderNames = folderByFullName.Keys.ToList();
            var indexedFolders = await db.Directories
                .Where(d => folderNames.Contains(d.FullName))
                .Select(d => d.FullName)
                .ToListAsync(cancellationToken);
            foreach (var folder in indexedFolders)
            {
                if (folderByFullName.TryGetValue(folder, out var index))
                    result[index] = result[index] with { Indexed = true };
            }
        }

        if (byFullName.Count == 0)
            return result;

        var names = byFullName.Keys.ToList();
        var links = await db.FileTags
            .Where(l => names.Contains(l.File!.FullName))
            .Select(l => new { l.File!.FullName, l.TagId })
            .ToListAsync(cancellationToken);

        // One row per indexed file (the page's files at most): its presence, and the resolution and duration of media.
        var rows = await db.Files
            .Where(f => names.Contains(f.FullName))
            .Select(f => new { f.FullName, f.Width, f.Height, f.Duration })
            .ToListAsync(cancellationToken);
        foreach (var row in rows)
        {
            if (!byFullName.TryGetValue(row.FullName, out var index))
                continue;
            if (items[index].Kind is not (ItemKind.Image or ItemKind.Video or ItemKind.Audio))
            {
                result[index] = result[index] with { Indexed = true };
                continue;
            }

            var known = row.Width > 0 && row.Height > 0;
            result[index] = result[index] with
            {
                Indexed = true,
                Width = known ? row.Width : null,
                Height = known ? row.Height : null,
                Duration = row.Duration > 0 ? row.Duration : null,
            };
        }

        if (links.Count > 0)
        {
            var graph = await TagGraph.LoadAsync(db, cancellationToken);
            foreach (var file in links.GroupBy(l => l.FullName, LibraryPaths.PathComparer))
            {
                if (byFullName.TryGetValue(file.Key, out var index))
                    result[index] = result[index] with { Tags = Summaries(graph, file.Select(l => l.TagId)) };
            }
        }

        return result;
    }

    public static IReadOnlyList<TagSummary> Summaries(TagGraph graph, IEnumerable<int> tagIds) =>
        TagGraph.Sort(tagIds.Distinct().Select(graph.Summary).OfType<TagSummary>());

    private static FileTags ToFileTags(TagGraph graph, string path, IEnumerable<int> tagIds)
    {
        var ids = tagIds.Distinct().ToList();
        return new FileTags(path, Summaries(graph, ids), graph.GroupByCategory(ids));
    }

    private bool TryResolveFile(string? relativePath, [NotNullWhen(true)] out string? fullPath) =>
        paths.TryResolve(relativePath, out fullPath) && File.Exists(fullPath);

    private static Dictionary<string, string[]> Error(string field, string message) => new() { [field] = [message] };
}
