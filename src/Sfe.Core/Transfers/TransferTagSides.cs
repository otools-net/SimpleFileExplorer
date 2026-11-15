using Otools.Net.Sfe.Core.Connections.Indexing;

namespace Otools.Net.Sfe.Core.Transfers;

/// <summary>
/// What carries the library's tags across a transfer: the library's files (<see cref="LibraryTransferTags"/>), the
/// rows of connection files in the library's index (<see cref="RemoteFileTags"/>), and the index itself, started
/// for a connection the library doesn't index yet (its files have no rows to tag before).
/// </summary>
public sealed record TransferTagSides(
    LibraryTransferTags Library,
    RemoteFileTags Connections,
    ConnectionIndexes Indexes
);
