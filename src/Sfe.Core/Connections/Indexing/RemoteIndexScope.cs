namespace Otools.Net.Sfe.Core.Connections.Indexing;

/// <summary>What a batch of <see cref="RemoteIndexWriter"/> writes for.</summary>
/// <param name="Pass">The connection's current listing pass (<see cref="Data.RemoteIndexState.Pass"/>).</param>
/// <param name="Following">The changes are read (a new folder comes with its items, so it is complete), or SFE made
/// the change; false during a full listing (a folder met is still to list).</param>
/// <param name="BySfe">SFE changed the item (<see cref="ConnectionIndexUpdates"/>): the folders on its way are not
/// "seen by the pass", which still has to list those it hasn't reached.</param>
/// <param name="PassUnderWay">A full listing of the connection goes on while SFE writes.</param>
internal sealed record RemoteIndexScope(
    int ConnectionId,
    bool CaseInsensitive,
    int Pass,
    bool Following,
    bool BySfe = false,
    bool PassUnderWay = false
);
