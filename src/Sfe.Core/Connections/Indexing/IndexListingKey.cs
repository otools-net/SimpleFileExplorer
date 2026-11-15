using Otools.Net.Sfe.Core.Library;

namespace Otools.Net.Sfe.Core.Connections.Indexing;

/// <summary>Where a page of an index listing begins: the folder's row, the order, the filter and the offset.</summary>
/// <param name="Filter">What folder rules leave out, as a text (<see cref="IndexListingFilter.Signature"/>).</param>
internal sealed record IndexListingKey(
    int ConnectionId,
    long FolderId,
    SortField Sort,
    bool Descending,
    string Filter,
    int Offset
);
