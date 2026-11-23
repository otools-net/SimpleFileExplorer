using Otools.Net.Sfe.Core.Access;
using Otools.Net.Sfe.Core.Transfers;

namespace Otools.Net.Sfe.Core.Connections.Indexing;

/// <summary>
/// The server's services a library's index of connections uses (registered by <c>AddSfeAccess</c>, handed to each
/// library like <c>FfmpegLimits</c>: agents/docs/architecture/tenants.md). Absent in the Core's own tests: then
/// nothing is synced.
/// </summary>
/// <param name="Calls">Every provider call of a sync goes through the connection's gate.</param>
/// <param name="Changes">What SFE changed in connections, written into the index at once.</param>
/// <param name="Access">Its count moves when a connection is removed or offered to fewer libraries.</param>
/// <param name="Listings">The server's kept listings of connections, forgotten when changes made outside SFE come.
/// </param>
public sealed record ConnectionIndexSources(
    ConnectionService Connections,
    ConnectionProviders Providers,
    TransferCalls Calls,
    ConnectionChanges Changes,
    AccessChanges Access,
    RemoteListings Listings
);
