using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sfe.Core.Access;
using Sfe.Core.Data;
using Sfe.Core.Library;
using Sfe.Core.Paths;
using Sfe.Core.Search;

namespace Sfe.Core.Tagging;

/// <summary>A default tag a folder gets from one of its parents.</summary>
/// <param name="Path">The parent folder it is set on (root-relative; empty for the root).</param>
public sealed record InheritedDefaultTag(string Path, TagSummary Tag);

/// <summary>The default tags of a folder: its own, and those of its parents, which apply to it too.</summary>
public sealed record FolderDefaults(string Path, IReadOnlyList<TagSummary> Own, IReadOnlyList<InheritedDefaultTag> Inherited)
{
    public bool IsEmpty => Own.Count == 0 && Inherited.Count == 0;
}

/// <summary>A default tag of a folder taken with it to the trash (<see cref="TrashEntry.DefaultTags"/>).</summary>
/// <param name="Path">The folder it is set on, relative to the folder deleted (empty: that folder itself).</param>
public sealed record KeptDefaultTag(string Path, int TagId, DateTime Created);

public enum DefaultTagChangeStatus
{
    Ok,
    Invalid,
    NotFound,
}

/// <summary>
/// The default tags of folders (agents/docs/features/default-tags.md): every file entering a folder, or one of its
/// sub-folders, gets them once, when it enters the index there (a scan, a new file, an upload, a copy:
/// <see cref="Indexing.DirectoryIndexer"/>) or is moved there (<see cref="Files.PathMoves"/>). A file whose tag is
/// removed later keeps it removed: nothing gives it back but entering another folder that has it.
/// </summary>
/// <remarks>
/// A library has a few of them: the rows are read once into memory and read again after a change, so that a scan asks
/// for the tags of each folder it visits without a round trip (a walk up the path, O(depth)).
/// </remarks>
public sealed class FolderDefaultTags(IDbContextFactory<SfeDbContext> dbFactory, LibraryPaths paths, ILogger<FolderDefaultTags> logger)
{
    private volatile Snapshot? _snapshot;
    private int _version;

    /// <summary>The tags of each folder that has some (root-relative paths, compared as the file system does).</summary>
    private sealed class Snapshot(Dictionary<string, int[]> byFolder)
    {
        public bool IsEmpty => byFolder.Count == 0;

        public int[] Own(string folder) => byFolder.GetValueOrDefault(folder) ?? [];

        /// <summary>The folder and each of its parents up to the root that has default tags, nearest first.</summary>
        public IEnumerable<(string Folder, int[] Tags)> Through(string folder)
        {
            for (var path = folder; ; path = Parent(path))
            {
                if (byFolder.TryGetValue(path, out var tags))
                    yield return (path, tags);
                if (path.Length == 0)
                    yield break;
            }
        }

        public int[] At(string folder) => IsEmpty ? [] : [.. Through(folder).SelectMany(f => f.Tags).Distinct()];
    }

    /// <summary>Default tags changed (or a tag was deleted, taking its rows with it): read them again when next needed.</summary>
    public void Invalidate()
    {
        Interlocked.Increment(ref _version);
        _snapshot = null;
    }

    private async Task<Snapshot> SnapshotAsync(CancellationToken cancellationToken)
    {
        if (_snapshot is { } snapshot)
            return snapshot;
        var version = Volatile.Read(ref _version);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var rows = await db.FolderDefaultTags.AsNoTracking().Select(d => new { d.Path, d.TagId }).ToListAsync(cancellationToken);
        snapshot = new Snapshot(rows
            .GroupBy(r => r.Path, LibraryPaths.PathComparer)
            .ToDictionary(g => g.Key, g => g.Select(r => r.TagId).Distinct().ToArray(), LibraryPaths.PathComparer));
        // Changed while reading: this one may miss the change, keep it for this call only.
        if (Volatile.Read(ref _version) == version)
            _snapshot = snapshot;
        return snapshot;
    }

    /// <summary>The tags a file entering the folder <paramref name="fullFolder"/> gets: the folder's and its parents'.</summary>
    public async Task<int[]> ForFolderAsync(string fullFolder, CancellationToken cancellationToken = default)
    {
        var snapshot = await SnapshotAsync(cancellationToken);
        return snapshot.IsEmpty || paths.ToRelative(fullFolder) is not { } folder ? [] : snapshot.At(folder);
    }

    /// <summary>The default tags of a folder (root-relative), its own and its parents'.</summary>
    public async Task<FolderDefaults> GetAsync(string folder, CancellationToken cancellationToken = default)
    {
        var snapshot = await SnapshotAsync(cancellationToken);
        if (snapshot.IsEmpty)
            return new FolderDefaults(folder, [], []);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var graph = await TagGraph.LoadAsync(db, cancellationToken);
        return Describe(graph, snapshot, folder);
    }

    private static FolderDefaults Describe(TagGraph graph, Snapshot snapshot, string folder)
    {
        var own = Summaries(graph, snapshot.Own(folder));
        var seen = own.Select(t => t.Id).ToHashSet();
        var inherited = new List<InheritedDefaultTag>();
        foreach (var (parent, tags) in snapshot.Through(folder).Where(f => !LibraryPaths.PathComparer.Equals(f.Folder, folder)))
            inherited.AddRange(Summaries(graph, tags).Where(t => seen.Add(t.Id)).Select(t => new InheritedDefaultTag(parent, t)));
        return new FolderDefaults(folder, own, inherited);
    }

    private static List<TagSummary> Summaries(TagGraph graph, IEnumerable<int> tagIds) =>
        [.. tagIds.Select(graph.Summary).OfType<TagSummary>().OrderBy(t => t.DisplayName, StringComparer.CurrentCultureIgnoreCase)];

    /// <summary>
    /// A listing with the default tags of its folder (<see cref="DirectoryListing.DefaultTags"/>) and the own ones of
    /// its folders (<see cref="LibraryItem.DefaultTags"/>): O(page), one lookup per folder of the page.
    /// </summary>
    public async Task<DirectoryListing> MarkAsync(DirectoryListing listing, CancellationToken cancellationToken = default)
    {
        var snapshot = await SnapshotAsync(cancellationToken);
        if (snapshot.IsEmpty)
            return listing;
        var here = snapshot.Through(listing.Path).Any();
        var folders = listing.Items.Where(i => i.Kind == ItemKind.Directory && snapshot.Own(i.Path).Length > 0).ToList();
        if (!here && folders.Count == 0)
            return listing;

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var graph = await TagGraph.LoadAsync(db, cancellationToken);
        return listing with
        {
            DefaultTags = here ? Describe(graph, snapshot, listing.Path) : null,
            Items = folders.Count == 0 ? listing.Items : [.. listing.Items.Select(item => item.Kind == ItemKind.Directory && snapshot.Own(item.Path) is { Length: > 0 } own
                ? item with { DefaultTags = Summaries(graph, own) }
                : item)],
        };
    }

    /// <summary>Adds and removes default tags of a folder (root-relative; it must exist).</summary>
    public async Task<(DefaultTagChangeStatus Status, string? Error)> ChangeAsync(
        string folder, IReadOnlyCollection<int> add, IReadOnlyCollection<int> remove, CancellationToken cancellationToken = default)
    {
        if (!paths.TryResolve(folder, out var full) || !Directory.Exists(full))
            return (DefaultTagChangeStatus.NotFound, null);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var graph = await TagGraph.LoadAsync(db, cancellationToken);
        if (add.Concat(remove).FirstOrDefault(id => !graph.Contains(id)) is var unknown and not 0)
            return (DefaultTagChangeStatus.Invalid, $"Tag {unknown} does not exist.");

        try
        {
            if (remove.Count > 0)
                await db.FolderDefaultTags.Where(d => d.Path == folder && remove.Contains(d.TagId)).ExecuteDeleteAsync(cancellationToken);
            if (add.Count > 0)
            {
                var existing = await db.FolderDefaultTags.Where(d => d.Path == folder).Select(d => d.TagId).ToListAsync(cancellationToken);
                foreach (var tagId in add.Except(existing).Except(remove))
                    db.FolderDefaultTags.Add(new FolderDefaultTag { Path = folder, TagId = tagId, Created = DateTime.UtcNow });
                await db.SaveChangesAsync(cancellationToken);
            }
        }
        finally
        {
            Invalidate();
        }
        return (DefaultTagChangeStatus.Ok, null);
    }

    /// <summary>
    /// The default tags of <paramref name="from"/> and of the folders below it follow the folder to
    /// <paramref name="to"/> (root-relative; renamed or moved). Rows the new place had already are kept.
    /// </summary>
    public async Task MoveAsync(string from, string to, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        // A few rows per library: read them all and compare as the file system does.
        var rows = await db.FolderDefaultTags.ToListAsync(cancellationToken);
        var taken = rows.Where(r => !FolderPolicy.IsSameOrBelow(r.Path, from)).Select(r => (r.Path, r.TagId)).ToHashSet(PathTagComparer.Instance);
        var moved = 0;
        foreach (var row in rows.Where(r => FolderPolicy.IsSameOrBelow(r.Path, from)))
        {
            row.Path = to + row.Path[from.Length..];
            if (taken.Add((row.Path, row.TagId)))
                moved++;
            else
                db.FolderDefaultTags.Remove(row);
        }
        if (await db.SaveChangesAsync(cancellationToken) > 0)
        {
            Invalidate();
            logger.LogInformation("{Count} default tags follow {From} to {To}", moved, from, to);
        }
    }

    /// <summary>
    /// A file or folder moved from <paramref name="fromFull"/> to <paramref name="toFull"/> (its index rows already
    /// there): its files get the default tags of the new place they didn't have from the old one. A rename in the same
    /// folder changes nothing.
    /// </summary>
    /// <returns>Links added.</returns>
    public async Task<int> ApplyAfterMoveAsync(string fromFull, string toFull, bool isFolder, CancellationToken cancellationToken = default)
    {
        var snapshot = await SnapshotAsync(cancellationToken);
        if (snapshot.IsEmpty || paths.ToRelative(fromFull) is not { Length: > 0 } from || paths.ToRelative(toFull) is not { Length: > 0 } to)
            return 0;
        var tags = snapshot.At(Parent(to)).Except(snapshot.At(Parent(from))).ToList();
        if (tags.Count == 0)
            return 0;

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var files = isFolder
            ? db.Files.AsNoTracking().Where(SearchPredicateBuilder.InTree<FileEntry>(f => f.FullName, toFull))
            : db.Files.AsNoTracking().Where(f => f.FullName == toFull);
        return await FileTagRows.AddToAllAsync(db, files, tags, cancellationToken);
    }

    /// <summary>
    /// Takes the default tags of a folder and below it off the table (it goes to the trash, or for good): returned to be
    /// kept with it and given back by <see cref="PutBackAsync"/>.
    /// </summary>
    public async Task<IReadOnlyList<KeptDefaultTag>> TakeTreeAsync(string folder, CancellationToken cancellationToken = default)
    {
        if (folder.Length == 0)
            return [];
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var rows = (await db.FolderDefaultTags.ToListAsync(cancellationToken)).Where(r => FolderPolicy.IsSameOrBelow(r.Path, folder)).ToList();
        if (rows.Count == 0)
            return [];
        db.FolderDefaultTags.RemoveRange(rows);
        await db.SaveChangesAsync(cancellationToken);
        Invalidate();
        return [.. rows.Select(r => new KeptDefaultTag(r.Path[folder.Length..].TrimStart('/'), r.TagId, r.Created))];
    }

    /// <summary>Gives back tags taken by <see cref="TakeTreeAsync"/> to the folder now at <paramref name="folder"/> (restored).</summary>
    /// <returns>Rows put back (tags deleted meanwhile are dropped).</returns>
    public async Task<int> PutBackAsync(string folder, IReadOnlyList<KeptDefaultTag> kept, CancellationToken cancellationToken = default)
    {
        if (kept.Count == 0 || folder.Length == 0)
            return 0;
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var known = (await db.Tags.Select(t => t.Id).ToListAsync(cancellationToken)).ToHashSet();
        var existing = (await db.FolderDefaultTags.Select(d => new { d.Path, d.TagId }).ToListAsync(cancellationToken))
            .Select(d => (d.Path, d.TagId))
            .ToHashSet(PathTagComparer.Instance);
        foreach (var row in kept.Where(k => known.Contains(k.TagId)))
        {
            var path = row.Path.Length == 0 ? folder : $"{folder}/{row.Path}";
            if (existing.Add((path, row.TagId)))
                db.FolderDefaultTags.Add(new FolderDefaultTag { Path = path, TagId = row.TagId, Created = row.Created });
        }
        var added = await db.SaveChangesAsync(cancellationToken);
        if (added > 0)
            Invalidate();
        return added;
    }

    private static string Parent(string path) => path.LastIndexOf('/') is var slash and >= 0 ? path[..slash] : string.Empty;

    private sealed class PathTagComparer : IEqualityComparer<(string Path, int TagId)>
    {
        public static readonly PathTagComparer Instance = new();

        public bool Equals((string Path, int TagId) x, (string Path, int TagId) y) =>
            x.TagId == y.TagId && LibraryPaths.PathComparer.Equals(x.Path, y.Path);

        public int GetHashCode((string Path, int TagId) obj) => HashCode.Combine(LibraryPaths.PathComparer.GetHashCode(obj.Path), obj.TagId);
    }
}
