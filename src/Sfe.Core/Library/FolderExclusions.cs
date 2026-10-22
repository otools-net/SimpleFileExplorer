using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using Sfe.Core.Options;
using Sfe.Core.Paths;
using Sfe.Core.Settings;

namespace Sfe.Core.Library;

/// <summary>What a folder of the library can be kept out of.</summary>
public enum ExclusionKind
{
    /// <summary>Scans (<see cref="ScanOptions.ExcludedFolders"/>): what is indexed there stays as it is.</summary>
    Scan,

    /// <summary>Trickplay (<see cref="TrickplayOptions.ExcludedFolders"/>): no new sheets.</summary>
    Trickplay,

    /// <summary>Thumbnails (<see cref="ThumbnailOptions.ExcludedFolders"/>): no new thumbnails.</summary>
    Thumbnails,
}

/// <summary>
/// For each kind, the listed folder (root-relative) a folder is excluded through: itself or the nearest of its
/// ancestors on the list; null when not excluded.
/// </summary>
public sealed record FolderExclusionState(string? Scan, string? Trickplay, string? Thumbnails)
{
    public static readonly FolderExclusionState None = new(null, null, null);

    [JsonIgnore]
    public bool IsNone => Scan is null && Trickplay is null && Thumbnails is null;

    public string? Of(ExclusionKind kind) => kind switch
    {
        ExclusionKind.Scan => Scan,
        ExclusionKind.Trickplay => Trickplay,
        _ => Thumbnails,
    };
}

/// <summary>
/// The folder lists of a library's settings that keep folders, with their sub-folders, out of scans, trickplay and
/// thumbnails. Compiled once per options instance (a settings change brings a new one): a lookup walks up the path's
/// segments, O(depth).
/// </summary>
public sealed class FolderExclusions
{
    private static readonly ConditionalWeakTable<SfeOptions, FolderExclusions> Cache = [];

    private readonly List<string>[] _sources;
    private readonly HashSet<string>[] _folders;

    private FolderExclusions(SfeOptions options)
    {
        _sources = Enum.GetValues<ExclusionKind>().Select(kind => List(options, kind)).ToArray();
        _folders = _sources
            .Select(list => new HashSet<string>(
                list.Select(LibraryPaths.NormalizeRelative).OfType<string>().Where(f => f.Length > 0),
                LibraryPaths.PathComparer))
            .ToArray();
    }

    /// <summary>The lists of <paramref name="options"/> (compiled again when a list was replaced).</summary>
    public static FolderExclusions Of(SfeOptions options)
    {
        if (Cache.TryGetValue(options, out var cached)
            && Enum.GetValues<ExclusionKind>().All(kind => ReferenceEquals(cached._sources[(int)kind], List(options, kind))))
            return cached;
        var compiled = new FolderExclusions(options);
        Cache.AddOrUpdate(options, compiled);
        return compiled;
    }

    /// <summary>The setting key of a kind's list.</summary>
    public static string Key(ExclusionKind kind) => kind switch
    {
        ExclusionKind.Scan => "scan.excludedFolders",
        ExclusionKind.Trickplay => "trickplay.excludedFolders",
        _ => "thumbnails.excludedFolders",
    };

    public static List<string> List(SfeOptions options, ExclusionKind kind) => kind switch
    {
        ExclusionKind.Scan => options.Scan.ExcludedFolders,
        ExclusionKind.Trickplay => options.Trickplay.ExcludedFolders,
        _ => options.Thumbnails.ExcludedFolders,
    };

    public static List<string> List(EditableSettings settings, ExclusionKind kind) => kind switch
    {
        ExclusionKind.Scan => settings.Scan.ExcludedFolders,
        ExclusionKind.Trickplay => settings.Trickplay.ExcludedFolders,
        _ => settings.Thumbnails.ExcludedFolders,
    };

    /// <summary>No folder is excluded from anything (the fast path of listings).</summary>
    public bool IsEmpty => _folders.All(f => f.Count == 0);

    /// <summary>Every folder on at least one list, once (as first listed), in path order.</summary>
    public IReadOnlyList<string> Folders() =>
        [.. _folders.SelectMany(f => f).Distinct(LibraryPaths.PathComparer).Order(LibraryPaths.PathComparer)];

    /// <summary><paramref name="relativePath"/> is on the list of <paramref name="kind"/> itself.</summary>
    public bool Lists(ExclusionKind kind, string relativePath) => _folders[(int)kind].Contains(relativePath);

    /// <summary>The folder (root-relative, canonical) is excluded from <paramref name="kind"/>, itself or through an ancestor.</summary>
    public bool IsExcluded(ExclusionKind kind, string relativePath) => Through(kind, relativePath) is not null;

    /// <summary>A file or folder given by its full path is excluded from <paramref name="kind"/>; false outside the library.</summary>
    public bool IsExcluded(ExclusionKind kind, LibraryPaths paths, string fullPath) =>
        _folders[(int)kind].Count > 0 && paths.ToRelative(fullPath) is { } relative && IsExcluded(kind, relative);

    /// <summary>The listed folder <paramref name="relativePath"/> is excluded through, the nearest first; null when it isn't.</summary>
    public string? Through(ExclusionKind kind, string relativePath)
    {
        var folders = _folders[(int)kind];
        if (folders.Count == 0)
            return null;
        for (var path = relativePath; path.Length > 0; path = Parent(path))
            if (folders.Contains(path))
                return path;
        return null;
    }

    /// <summary>
    /// The listing with what its folder and its sub-folders are kept out of: O(page), one lookup per folder of the
    /// page and a walk up from the folder.
    /// </summary>
    public DirectoryListing Mark(DirectoryListing listing)
    {
        if (IsEmpty)
            return listing;
        var here = At(listing.Path);
        return listing with
        {
            Exclusions = here.IsNone ? null : here,
            Items = [.. listing.Items.Select(item => item.Kind == ItemKind.Directory && Child(here, item.Path) is { IsNone: false } state
                ? item with { Excluded = state }
                : item)],
        };
    }

    /// <summary>Every kind at once, for a folder (root-relative, canonical).</summary>
    public FolderExclusionState At(string relativePath) => IsEmpty
        ? FolderExclusionState.None
        : new FolderExclusionState(
            Through(ExclusionKind.Scan, relativePath),
            Through(ExclusionKind.Trickplay, relativePath),
            Through(ExclusionKind.Thumbnails, relativePath));

    /// <summary>
    /// The state of a child of a folder whose own state is <paramref name="parent"/>: what the parent has, or the
    /// child itself when listed. O(1), for the folders of a listing.
    /// </summary>
    public FolderExclusionState Child(FolderExclusionState parent, string childPath) => IsEmpty
        ? FolderExclusionState.None
        : new FolderExclusionState(
            parent.Scan ?? (Lists(ExclusionKind.Scan, childPath) ? childPath : null),
            parent.Trickplay ?? (Lists(ExclusionKind.Trickplay, childPath) ? childPath : null),
            parent.Thumbnails ?? (Lists(ExclusionKind.Thumbnails, childPath) ? childPath : null));

    private static string Parent(string path) => path.LastIndexOf('/') is var slash and >= 0 ? path[..slash] : string.Empty;
}
