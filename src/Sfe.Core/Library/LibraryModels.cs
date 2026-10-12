using Sfe.Core.Tagging;

namespace Sfe.Core.Library;

public enum ItemKind
{
    Directory,
    Image,
    Video,
    Audio,
    File,
}

public enum SortField
{
    Name,
    Modified,
    Size,
    Type,

    /// <summary>Full path, grouping search results by folder; the same as <see cref="Name"/> when browsing.</summary>
    Path,
}

/// <param name="Path">Path relative to the library root, '/' separated.</param>
/// <param name="Directory">Parent folder (relative), only filled for search results.</param>
/// <param name="Tags">Tags set on a file (not inherited ones); null for folders, or when the index is unavailable.</param>
/// <param name="Width">Pixel width of an image or video, when the index knows it (see <see cref="Data.FileEntry.Width"/>).</param>
/// <param name="Height">Pixel height, set along with <paramref name="Width"/>.</param>
/// <param name="Duration">Seconds of a video or audio file, when the index knows them (see <see cref="Data.FileEntry.Duration"/>).</param>
public sealed record LibraryItem(
    string Name,
    string Path,
    ItemKind Kind,
    string? Extension,
    long? Size,
    DateTimeOffset? Modified,
    string? Directory = null,
    IReadOnlyList<TagSummary>? Tags = null,
    int? Width = null,
    int? Height = null,
    double? Duration = null);

public sealed record Breadcrumb(string Name, string Path);

public sealed record DirectoryListing(
    string Path,
    string Name,
    string? ParentPath,
    IReadOnlyList<Breadcrumb> Breadcrumbs,
    IReadOnlyList<LibraryItem> Items,
    int Offset,
    int Limit,
    int Total,
    int DirectoryCount,
    int FileCount);

/// <param name="Previous">The folder listed before this one in its parent; null for the first one or the root.</param>
/// <param name="Next">The folder listed after this one; null for the last one or the root.</param>
/// <param name="Position">1-based place of the folder among its parent's folders; 0 when not listed there (root).</param>
/// <param name="Count">Number of folders in the parent.</param>
public sealed record FolderSiblings(
    string Path,
    LibraryItem? Previous,
    LibraryItem? Next,
    int Position,
    int Count);

/// <param name="Path">The folder, relative to the library root.</param>
/// <param name="Items">Its first pictures (images, videos with thumbnails) in name order; empty when it has none.</param>
public sealed record FolderCover(string Path, IReadOnlyList<LibraryItem> Items);

/// <param name="Next">Value of the <c>after</c> parameter that requests the next page; null on the last page.</param>
/// <param name="Total">
/// Number of matches, counted on the first page only (null on the next ones) and at most
/// <see cref="Search.FileSearchService.CountLimit"/>.
/// </param>
/// <param name="TotalCapped">The count stopped at the limit: there are more matches than <paramref name="Total"/>.</param>
public sealed record SearchResults(
    string Query,
    string Path,
    IReadOnlyList<LibraryItem> Items,
    int Limit,
    bool HasMore,
    string? Next,
    int? Total = null,
    bool TotalCapped = false);
