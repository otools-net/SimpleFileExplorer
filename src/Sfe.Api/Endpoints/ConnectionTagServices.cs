using Otools.Net.Sfe.Core.Connections.Indexing;

namespace Otools.Net.Sfe.Api.Endpoints;

/// <summary>What tagging a connection's files uses (bound with <c>[AsParameters]</c>).</summary>
/// <param name="Updates">Writes the row of a file the index doesn't hold yet, as SFE's own changes do.</param>
/// <param name="Indexes">Starts the library's index of a connection it never indexed.</param>
internal sealed record ConnectionTagServices(
    RemoteFileTags Tags,
    ConnectionIndexUpdates Updates,
    RemoteIndexTransactions Store,
    ConnectionIndexes Indexes
);
