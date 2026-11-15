using Otools.Net.Sfe.Core.Library;

namespace Otools.Net.Sfe.Core.Connections.Indexing;

/// <summary>A page of a search of a connection's files (<see cref="RemoteIndexSearch"/>).</summary>
/// <param name="Results">As the library's search answers: paths relative to the connection's root.</param>
/// <param name="ProviderIds">The provider's id of each item of <paramref name="Results"/> (null when it has none),
/// for the last check of folder rules.</param>
/// <param name="Coverage">How much of the connection the index holds.</param>
public sealed record RemoteSearchPage(
    SearchResults Results,
    IReadOnlyList<string?> ProviderIds,
    RemoteSearchCoverage Coverage
);
