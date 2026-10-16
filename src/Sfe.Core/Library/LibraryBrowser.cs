using Microsoft.Extensions.Options;
using Sfe.Core.Options;
using Sfe.Core.Paths;

namespace Sfe.Core.Library;

/// <summary>
/// What a listing leaves out (folder rules): the entries named in <paramref name="Hidden"/>, or, when
/// <paramref name="Only"/> is set, every entry but those (a folder shown only to lead to what is below it). Names
/// compare as the host file system does.
/// </summary>
public sealed record ListingFilter(IReadOnlySet<string> Hidden, IReadOnlySet<string>? Only = null)
{
    public bool Shows(string name) => !Hidden.Contains(name) && (Only is null || Only.Contains(name));
}

/// <summary>Lists folders straight from disk.</summary>
public sealed class LibraryBrowser(LibraryPaths paths, MediaClassifier classifier, IOptionsMonitor<SfeOptions> options)
{
    private static readonly EnumerationOptions WithHidden = CreateEnumerationOptions(showHidden: true);
    private static readonly EnumerationOptions WithoutHidden = CreateEnumerationOptions(showHidden: false);

    /// <summary>How folders are listed and scanned; follows the "show hidden items" setting.</summary>
    public EnumerationOptions EnumerationOptions => options.CurrentValue.ShowHiddenItems ? WithHidden : WithoutHidden;

    /// <summary>Sorted listings kept for the next pages of a folder (most recently used last).</summary>
    private const int CachedListings = 8;

    private readonly Lock _cacheGate = new();
    private readonly List<CachedListing> _cache = [];

    private sealed record ListingKey(string FullPath, SortField Sort, bool Descending, bool ShowHidden);

    /// <param name="Version">Last write time of the folder: it changes when an entry is added, removed or renamed.</param>
    private sealed record CachedListing(ListingKey Key, DateTime Version, List<LibraryItem> Items, int DirectoryCount)
    {
        private Dictionary<string, int>? _positions;

        /// <summary>Where each name is in <see cref="Items"/>: built once, for the listings that folder rules filter.</summary>
        public Dictionary<string, int> Positions => _positions ??= Items
            .Select((item, index) => (item.Name, index))
            .DistinctBy(e => e.Name, LibraryPaths.PathComparer)
            .ToDictionary(e => e.Name, e => e.index, LibraryPaths.PathComparer);
    }

    /// <param name="filter">What folder rules leave out; offsets and counts are those of what is left.</param>
    /// <returns>null when the folder does not exist or is outside the library.</returns>
    /// <remarks>
    /// The first page (offset 0) lists the folder from disk. The next pages reuse that sorted listing while the
    /// folder is unchanged: scrolling through a folder of a hundred thousand entries lists and sorts it once instead
    /// of once per page. Sizes and dates on later pages are those read for the first page. A filter costs what it
    /// names (the few entries folder rules are about), not the size of the folder.
    /// </remarks>
    public DirectoryListing? List(string? relativePath, int offset, int limit, SortField sort = SortField.Name, bool descending = false,
        ListingFilter? filter = null)
    {
        if (!paths.TryResolve(relativePath, out var fullPath))
            return null;

        var directory = new DirectoryInfo(fullPath);
        if (!directory.Exists)
            return null;

        var listing = GetListing(directory, sort, descending, reuse: offset > 0);

        var path = paths.ToRelative(directory.FullName) ?? string.Empty;
        var (items, total, directories) = filter is null
            ? (listing.Items.Skip(offset).Take(limit).ToList(), listing.Items.Count, listing.DirectoryCount)
            : Filtered(listing, filter, offset, limit);

        return new DirectoryListing(
            Path: path,
            Name: path.Length == 0 ? paths.RootName : directory.Name,
            ParentPath: path.Length == 0 ? null : ParentOf(path),
            Breadcrumbs: BuildBreadcrumbs(path),
            Items: items,
            Offset: offset,
            Limit: limit,
            Total: total,
            DirectoryCount: directories,
            FileCount: total - directories);
    }

    /// <summary>A page of the listing without what <paramref name="filter"/> leaves out, with the counts of what is left.</summary>
    private static (List<LibraryItem> Items, int Total, int Directories) Filtered(CachedListing listing, ListingFilter filter, int offset, int limit)
    {
        var entries = listing.Items;
        var positions = listing.Positions;
        if (filter.Only is { } only)
        {
            var shown = only.Where(name => !filter.Hidden.Contains(name))
                .Select(name => positions.TryGetValue(name, out var index) ? index : -1)
                .Where(index => index >= 0)
                .Distinct()
                .Order()
                .Select(index => entries[index])
                .ToList();
            return (shown.Skip(offset).Take(limit).ToList(), shown.Count, shown.Count(i => i.Kind == ItemKind.Directory));
        }

        var hidden = filter.Hidden.Select(name => positions.TryGetValue(name, out var index) ? index : -1).Where(index => index >= 0).ToHashSet();
        var sorted = hidden.Order().ToList();
        // The entry at `offset` once the hidden ones are left out: each hidden one up to it moves it one further.
        var raw = offset;
        foreach (var index in sorted)
        {
            if (index > raw)
                break;
            raw++;
        }

        var page = new List<LibraryItem>(Math.Min(limit, Math.Max(0, entries.Count - raw)));
        for (; raw < entries.Count && page.Count < limit; raw++)
        {
            if (!hidden.Contains(raw))
                page.Add(entries[raw]);
        }
        return (page, entries.Count - hidden.Count, listing.DirectoryCount - sorted.Count(index => index < listing.DirectoryCount));
    }

    /// <summary>The folders before and after a folder in its parent, listed with the given sort.</summary>
    /// <param name="filter">What folder rules leave out of the parent's listing.</param>
    /// <returns>null when the folder does not exist or is outside the library; no siblings for the root.</returns>
    /// <remarks>
    /// Reuses the parent's sorted listing while the parent is unchanged: stepping folder after folder through a parent
    /// of many sub-folders lists it once.
    /// </remarks>
    public FolderSiblings? Siblings(string? relativePath, SortField sort = SortField.Name, bool descending = false, ListingFilter? filter = null)
    {
        if (!paths.TryResolve(relativePath, out var fullPath))
            return null;

        var directory = new DirectoryInfo(fullPath);
        if (!directory.Exists)
            return null;

        var path = paths.ToRelative(directory.FullName) ?? string.Empty;
        if (path.Length == 0 || directory.Parent is not { } parent)
            return new FolderSiblings(path, null, null, 0, 0);

        var listing = GetListing(parent, sort, descending, reuse: true);
        // Folders come first in a listing.
        var folders = listing.Items.Take(listing.DirectoryCount).Where(i => filter is null || filter.Shows(i.Name)).ToList();
        var index = folders.FindIndex(i => string.Equals(i.Path, path, LibraryPaths.PathComparison));
        if (index < 0)
            return new FolderSiblings(path, null, null, 0, folders.Count);   // hidden, or created since the listing

        return new FolderSiblings(
            Path: path,
            Previous: index > 0 ? folders[index - 1] : null,
            Next: index < folders.Count - 1 ? folders[index + 1] : null,
            Position: index + 1,
            Count: folders.Count);
    }

    /// <summary>Most pictures a folder card can show (<see cref="BrowseOptions.FolderPictures"/>).</summary>
    public const int MaxFolderPictures = 16;

    /// <summary>Folders one <see cref="Covers"/> call may name.</summary>
    public const int MaxCoverPaths = 100;

    /// <summary>
    /// Entries read at most to find a folder's cover: a folder of a hundred thousand files is not read in full for
    /// four pictures. NTFS lists in name order, so there the cover is the same; elsewhere it comes from the first ones.
    /// </summary>
    private const int MaxCoverEntries = 5000;

    /// <summary>Covers kept; all are dropped when full (each costs one folder read to rebuild).</summary>
    private const int CachedCovers = 4096;

    private readonly Lock _coversGate = new();
    private readonly Dictionary<CoverKey, CachedCover> _covers = [];

    private sealed record CoverKey(string FullPath, bool ShowHidden, bool Videos, int Count);

    private sealed record CachedCover(DateTime Version, IReadOnlyList<LibraryItem> Items);

    /// <summary>
    /// The first <see cref="BrowseOptions.FolderPictures"/> pictures (images, and videos when <paramref name="videos"/>)
    /// of each folder, in name order, for the folder cards of a listing. Only the folder's own files count, not its
    /// sub-folders. Empty lists when the setting is 0.
    /// </summary>
    /// <param name="videos">Include videos: only worth it when they have thumbnails.</param>
    /// <returns>One cover per existing folder of the library; the others are left out.</returns>
    /// <remarks>Kept while the folder is unchanged (its last write time changes when an entry is added or removed).</remarks>
    public IReadOnlyList<FolderCover> Covers(IEnumerable<string> relativePaths, bool videos)
    {
        var showHidden = options.CurrentValue.ShowHiddenItems;
        var count = Math.Clamp(options.CurrentValue.Browse.FolderPictures, 0, MaxFolderPictures);
        var covers = new List<FolderCover>();
        foreach (var relativePath in relativePaths)
        {
            if (!paths.TryResolve(relativePath, out var fullPath))
                continue;

            var directory = new DirectoryInfo(fullPath);
            if (!directory.Exists || paths.ToRelative(directory.FullName) is not { } path)
                continue;

            var key = new CoverKey(directory.FullName, showHidden, videos, count);
            var version = directory.LastWriteTimeUtc;
            CachedCover? cached;
            lock (_coversGate)
                _covers.TryGetValue(key, out cached);

            if (cached is null || cached.Version != version)
            {
                cached = new CachedCover(version, ReadCover(directory, videos, count));
                lock (_coversGate)
                {
                    if (_covers.Count >= CachedCovers)
                        _covers.Clear();
                    _covers[key] = cached;
                }
            }

            covers.Add(new FolderCover(path, cached.Items));
        }

        return covers;
    }

    private List<LibraryItem> ReadCover(DirectoryInfo directory, bool videos, int count)
    {
        // The smallest names so far, in order: a short insertion sort instead of sorting the whole folder.
        var cover = new List<LibraryItem>(count + 1);
        if (count == 0)
            return cover;

        try
        {
            foreach (var file in directory.EnumerateFiles("*", EnumerationOptions).Take(MaxCoverEntries))
            {
                var kind = classifier.Classify(file.Extension);
                if (kind is not (ItemKind.Image or ItemKind.Video) || (kind == ItemKind.Video && !videos))
                    continue;
                if (cover.Count == count && NaturalComparer.Instance.Compare(file.Name, cover[^1].Name) >= 0)
                    continue;
                if (ToItem(file) is not { } item)
                    continue;

                var index = cover.FindIndex(c => NaturalComparer.Instance.Compare(item.Name, c.Name) < 0);
                cover.Insert(index < 0 ? cover.Count : index, item);
                if (cover.Count > count)
                    cover.RemoveAt(count);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Unreadable or removed meanwhile: a card without pictures.
        }

        return cover;
    }

    /// <param name="reuse">Take the cached listing when the folder is unchanged; false lists it from disk anew.</param>
    private CachedListing GetListing(DirectoryInfo directory, SortField sort, bool descending, bool reuse)
    {
        var key = new ListingKey(directory.FullName, sort, descending, options.CurrentValue.ShowHiddenItems);
        var version = directory.LastWriteTimeUtc;
        var listing = reuse ? FindCached(key, version) : null;
        if (listing is null)
        {
            var items = Sort(directory.EnumerateFileSystemInfos("*", EnumerationOptions).Select(ToItem).OfType<LibraryItem>(), sort, descending)
                .ToList();
            listing = new CachedListing(key, version, items, items.Count(e => e.Kind == ItemKind.Directory));
            Store(listing);
        }

        return listing;
    }

    private CachedListing? FindCached(ListingKey key, DateTime version)
    {
        lock (_cacheGate)
        {
            var index = _cache.FindIndex(c => c.Key == key);
            if (index < 0)
                return null;

            var cached = _cache[index];
            _cache.RemoveAt(index);
            if (cached.Version != version)
                return null;
            _cache.Add(cached);
            return cached;
        }
    }

    private void Store(CachedListing listing)
    {
        lock (_cacheGate)
        {
            _cache.RemoveAll(c => c.Key == listing.Key);
            _cache.Add(listing);
            if (_cache.Count > CachedListings)
                _cache.RemoveAt(0);
        }
    }

    public LibraryItem? ToItem(FileSystemInfo info)
    {
        var path = paths.ToRelative(info.FullName);
        if (path is null)
            return null;

        return info switch
        {
            DirectoryInfo => new LibraryItem(info.Name, path, ItemKind.Directory, null, null, info.LastWriteTimeUtc),
            FileInfo file => new LibraryItem(file.Name, path, classifier.Classify(file.Extension), NormalizeExtension(file.Extension), file.Length, file.LastWriteTimeUtc),
            _ => null,
        };
    }

    public IReadOnlyList<Breadcrumb> BuildBreadcrumbs(string relativePath)
    {
        var crumbs = new List<Breadcrumb> { new(paths.RootName, string.Empty) };
        var current = string.Empty;
        foreach (var segment in relativePath.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            current = current.Length == 0 ? segment : current + "/" + segment;
            crumbs.Add(new Breadcrumb(segment, current));
        }

        return crumbs;
    }

    public static string? NormalizeExtension(string? extension) =>
        string.IsNullOrEmpty(extension) ? null : extension.TrimStart('.').ToLowerInvariant();

    private static string ParentOf(string relativePath)
    {
        var index = relativePath.LastIndexOf('/');
        return index < 0 ? string.Empty : relativePath[..index];
    }

    private static IEnumerable<LibraryItem> Sort(IEnumerable<LibraryItem> items, SortField sort, bool descending)
    {
        // Folders always come first, whatever the sort.
        var ordered = items.OrderBy(i => i.Kind == ItemKind.Directory ? 0 : 1);
        return (sort, descending) switch
        {
            (SortField.Modified, false) => ordered.ThenBy(i => i.Modified).ThenBy(i => i.Name, NaturalComparer.Instance),
            (SortField.Modified, true) => ordered.ThenByDescending(i => i.Modified).ThenBy(i => i.Name, NaturalComparer.Instance),
            (SortField.Size, false) => ordered.ThenBy(i => i.Size).ThenBy(i => i.Name, NaturalComparer.Instance),
            (SortField.Size, true) => ordered.ThenByDescending(i => i.Size).ThenBy(i => i.Name, NaturalComparer.Instance),
            (SortField.Type, false) => ordered.ThenBy(i => i.Extension).ThenBy(i => i.Name, NaturalComparer.Instance),
            (SortField.Type, true) => ordered.ThenByDescending(i => i.Extension).ThenBy(i => i.Name, NaturalComparer.Instance),
            (_, false) => ordered.ThenBy(i => i.Name, NaturalComparer.Instance),
            (_, true) => ordered.ThenByDescending(i => i.Name, NaturalComparer.Instance),
        };
    }

    private static EnumerationOptions CreateEnumerationOptions(bool showHidden) => new()
    {
        IgnoreInaccessible = true,
        RecurseSubdirectories = false,
        AttributesToSkip = showHidden ? 0 : FileAttributes.Hidden | FileAttributes.System,
    };
}
