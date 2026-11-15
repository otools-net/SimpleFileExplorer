namespace Otools.Net.Sfe.Core.Connections.Indexing;

/// <summary>What a batch of <see cref="RemoteIndexWriter"/> writes for.</summary>
/// <param name="Pass">The connection's current listing pass (<see cref="Data.RemoteIndexState.Pass"/>).</param>
/// <param name="Following">The changes are read (a new folder comes with its items, so it is complete), or SFE made
/// the change; false during a full listing (a folder met is still to list).</param>
internal sealed record RemoteIndexScope(int ConnectionId, bool CaseInsensitive, int Pass, bool Following);
