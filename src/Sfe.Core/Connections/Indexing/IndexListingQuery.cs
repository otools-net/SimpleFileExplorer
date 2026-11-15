using Otools.Net.Sfe.Core.Library;

namespace Otools.Net.Sfe.Core.Connections.Indexing;

/// <summary>A page of a connection's folder asked of its index (<see cref="RemoteIndexListings"/>).</summary>
/// <param name="Query">The page, as <see cref="RemoteListings"/> is asked for it.</param>
/// <param name="Target">The connection as it is now (root folder, account, case rule): an index made of another
/// root or account is not read.</param>
/// <param name="Filter">The children folder rules leave out, or the only ones shown (by name); null for none.</param>
/// <param name="HiddenIds">The provider's ids of items hidden by a rule set on them at another path (they were
/// renamed or moved outside SFE).</param>
/// <param name="ProviderPaging">The provider's read of this folder is still paged (an earlier page came from it):
/// a later page the index has no place for comes from the provider too, in the same order.</param>
public sealed record IndexListingQuery(
    RemoteListingQuery Query,
    ConnectionIndexTarget Target,
    ListingFilter? Filter,
    IReadOnlySet<string> HiddenIds,
    bool ProviderPaging
);
