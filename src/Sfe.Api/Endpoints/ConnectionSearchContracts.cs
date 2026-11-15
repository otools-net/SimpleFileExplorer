using Microsoft.Extensions.Options;
using Otools.Net.Sfe.Core.Access;
using Otools.Net.Sfe.Core.Connections;
using Otools.Net.Sfe.Core.Connections.Indexing;
using Otools.Net.Sfe.Core.Library;
using Otools.Net.Sfe.Core.Options;

namespace Otools.Net.Sfe.Api.Endpoints;

/// <summary>A page of a search of a connection's files (<c>GET api/connections/{id}/search</c>).</summary>
public sealed record ConnectionSearchResults
{
    public required int ConnectionId { get; init; }

    /// <summary>The connection's name: its root folder's.</summary>
    public required string Name { get; init; }

    /// <summary>The provider's kind (<c>dropbox</c>).</summary>
    public required string Kind { get; init; }

    /// <summary>As <c>GET api/search</c> answers; paths relative to the connection's root.</summary>
    public required SearchResults Results { get; init; }

    /// <summary><c>read</c> or <c>write</c>: how far the session uses the connection.</summary>
    public required FolderLevel Access { get; init; }

    /// <summary>What its provider can do.</summary>
    public required ProviderCapabilities Capabilities { get; init; }

    /// <summary>How much of the connection the library's index holds: what a search can find.</summary>
    public required RemoteSearchCoverage Coverage { get; init; }
}

/// <summary>What the search of a connection's files uses (bound with <c>[AsParameters]</c>).</summary>
internal sealed record ConnectionSearchServices(
    RemoteIndexSearch Search,
    ConnectionIndexes Indexes,
    IOptionsMonitor<SfeOptions> Options
);
