using Otools.Net.Sfe.Core.Library;

namespace Otools.Net.Sfe.Core.Connections;

/// <summary>A page of a connection's folder, as <see cref="RemoteListings"/> is asked for it.</summary>
/// <param name="Path">Normalised (<see cref="RemotePaths.Normalize"/>), relative to the connection's root.</param>
public sealed record RemoteListingQuery(
    int ConnectionId,
    string Path,
    int Offset,
    int Limit,
    SortField Sort = SortField.Name,
    bool Descending = false
);

/// <summary>A page of a connection's folder.</summary>
/// <param name="Total">Items of the folder; while <paramref name="HasMore"/>, those read so far.</param>
/// <param name="DirectoryCount">Folders among <paramref name="Total"/>.</param>
/// <param name="Sorted">The sort asked for was applied (folders first); false for a folder too large to read whole,
/// listed in the provider's order.</param>
/// <param name="HasMore">The folder has more items than <paramref name="Total"/>: the next pages read them.</param>
public sealed record RemoteListing(
    IReadOnlyList<RemoteItem> Items,
    int Offset,
    int Total,
    int DirectoryCount,
    bool Sorted,
    bool HasMore
);
