using System.Linq.Expressions;
using Otools.Net.Sfe.Core.Search;

namespace Otools.Net.Sfe.Core.Data;

/// <summary>
/// A file of a connection in a library's index (agents/docs/development/connections-index-plan.md), from the
/// provider's metadata only. The search's columns have <see cref="FileEntry"/>'s names (<c>FullName</c>,
/// <c>Name</c>, <c>Extension</c>, <c>Size</c>, <c>LastWriteTime</c>, <c>Width</c>, <c>Height</c>, <c>Duration</c>,
/// <c>Pixels</c>, <c>TagLinks</c>), so that one search builder reads both. Known by the provider's id first: a file
/// renamed or moved keeps its row, and its tags.
/// </summary>
public sealed class RemoteFile : ISearchableFile<RemoteFile>
{
    public long Id { get; set; }

    /// <summary>The connection (server-wide id, in the accounts' database: no foreign key).</summary>
    public int ConnectionId { get; set; }

    public long DirectoryId { get; set; }
    public RemoteDirectory? Directory { get; set; }

    /// <summary>The provider's id of the file, stable across renames; null when it gives none.</summary>
    public string? ProviderId { get; set; }

    /// <summary>
    /// The path from the connection's root, compared as the provider compares names (lowered when it ignores case):
    /// the key, compared and sorted by.
    /// </summary>
    public string FullName { get; set; } = string.Empty;

    /// <summary>The path as the provider spells it, to show and to ask the provider with.</summary>
    public string DisplayPath { get; set; } = string.Empty;

    /// <summary>The name without its extension, as the provider spells it (as in <see cref="FileEntry"/>).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Extension including the leading dot (e.g. <c>.mp4</c>); empty for a file without one.</summary>
    public string Extension { get; set; } = string.Empty;

    /// <summary>
    /// The name (with its extension) as listings sort it (<see cref="Library.NaturalComparer.KeyOf"/>): ordinal order
    /// of this key is the natural order of the names. Null on rows indexed before it existed, until a sync fills it.
    /// </summary>
    public string? NameKey { get; set; }

    public long Size { get; set; }

    /// <summary>UTC, as the provider tells it (Dropbox: the date on its server).</summary>
    public DateTime LastWriteTime { get; set; }

    /// <summary>The provider's hash of it (<see cref="Connections.ProviderCapabilities.ContentHash"/>).</summary>
    public string? ContentHash { get; set; }

    /// <summary>False for what has no content to read (an online document of the provider).</summary>
    public bool Downloadable { get; set; } = true;

    /// <summary>Pixel width; null until known (a thumbnail or the content cache tells it, not a listing).</summary>
    public int? Width { get; set; }

    /// <summary>Pixel height, see <see cref="Width"/>.</summary>
    public int? Height { get; set; }

    /// <summary>Length in seconds of a video or audio file; null until known.</summary>
    public double? Duration { get; set; }

    /// <summary><see cref="Width"/> × <see cref="Height"/>, computed by the database (never written).</summary>
    public long? Pixels { get; private set; }

    /// <summary>The listing pass that last saw it (<see cref="RemoteIndexState.Pass"/>).</summary>
    public int Pass { get; set; }

    /// <summary>Gone at the provider: deleted at the end of the sync unless its id comes back.</summary>
    public bool Removed { get; set; }

    public List<RemoteFileTag> TagLinks { get; set; } = [];

    /// <summary>The search sorts names by their natural key, as the listings do (<see cref="NameKey"/>).</summary>
    public static Expression<Func<RemoteFile, string?>> SortName { get; } = f => f.NameKey;

    /// <summary>Rows indexed before <see cref="NameKey"/> existed have none until a sync fills it.</summary>
    public static bool SortNameMayBeMissing => true;

    public static Expression<Func<RemoteFile, bool>> IsTagged { get; } = f => f.TagLinks.Any();

    public static Expression<Func<RemoteFile, bool>> HasAnyTag(int[] ids) =>
        f => f.TagLinks.Any(l => ids.Contains(l.TagId));

    public override string ToString() => DisplayPath;
}
