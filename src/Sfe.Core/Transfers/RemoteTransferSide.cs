using Otools.Net.Sfe.Core.Connections;

namespace Otools.Net.Sfe.Core.Transfers;

/// <summary>A connection as one side of a transfer (<see cref="RemoteTransferEndpoint"/>).</summary>
/// <param name="Storage">Opened for the job, which disposes it.</param>
/// <param name="Access">The person's access, checked again at every item.</param>
/// <param name="Right"><c>files.move</c> or <c>files.copy</c>: what writing into the connection, and a move's
/// taking from it, needs where it happens.</param>
/// <param name="FolderGone">Told of a folder a move took away from the connection (its folder rules go).</param>
public sealed record RemoteTransferSide(
    int ConnectionId,
    IRemoteStorage Storage,
    ProviderCapabilities Capabilities,
    IConnectionJobAccess Access,
    string Right,
    Func<string, CancellationToken, Task>? FolderGone = null
);
