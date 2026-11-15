using Otools.Net.Sfe.Core.KeepAlive;

namespace Otools.Net.Sfe.Api.Endpoints;

/// <param name="Path">The folder changed (root-relative).</param>
/// <param name="Origin">The tab whose request changed it (`X-SFE-Tab`), which needs no notice; null from a background
/// job.</param>
public sealed record FolderChange(string Path, string? Origin);

/// <param name="State">How keep alive goes in the space: `none` (off, or nothing pinged), `ok`, `failing`.</param>
public sealed record KeepAliveSignal(KeepAliveState State);

/// <summary>Something came into or left the trash.</summary>
public sealed record TrashSignal;

/// <param name="Path">The folder changed, from the connection's root.</param>
/// <param name="Origin">The tab whose request changed it (`X-SFE-Tab`); null from a background job.</param>
/// <param name="Connection">The connection's id.</param>
public sealed record ConnectionFolderChange(string Path, string? Origin, int Connection);

/// <summary>What the change feed reads connections with (bound with <c>[AsParameters]</c>).</summary>
internal sealed record ChangeFeedConnections(
    Otools.Net.Sfe.Core.Connections.ConnectionChanges Changes,
    Otools.Net.Sfe.Core.Connections.ConnectionAccess Access,
    Otools.Net.Sfe.Core.Connections.ConnectionProviders Providers
);
