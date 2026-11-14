using Sfe.Core.Library;

namespace Sfe.Core.Connections;

/// <summary>What a <see cref="RemoteSnapshot"/> is the read of: a folder of a connection in one order.</summary>
internal sealed record RemoteSnapshotKey(int ConnectionId, string Path, SortField Sort, bool Descending);
