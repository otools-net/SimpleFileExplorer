using Sfe.Core.Connections;

namespace Sfe.Core.Transfers;

/// <summary>A connection as one side of a transfer (<see cref="RemoteTransferEndpoint"/>).</summary>
/// <param name="Storage">Opened for the job, which disposes it.</param>
/// <param name="Access">The person's access, checked again at every item.</param>
/// <param name="Right"><c>files.move</c> or <c>files.copy</c>: what writing into the connection, and a move's
/// taking from it, needs where it happens.</param>
public sealed record RemoteTransferSide(
    int ConnectionId,
    IRemoteStorage Storage,
    ProviderCapabilities Capabilities,
    IConnectionJobAccess Access,
    string Right
);
