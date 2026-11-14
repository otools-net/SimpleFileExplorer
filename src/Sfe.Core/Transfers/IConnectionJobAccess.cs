using Otools.Net.Sfe.Core.Connections;

namespace Otools.Net.Sfe.Core.Transfers;

/// <summary>
/// What the person who started a transfer may do in a connection, read again as the job goes: folder rules, groups,
/// rights, the account and the connection itself can change while it runs, long after the request that started it
/// (agents/docs/features/connections.md).
/// </summary>
public interface IConnectionJobAccess
{
    /// <summary>The person's folder access in the connection now; null when they may no longer use it.</summary>
    Task<ConnectionFolders?> FoldersAsync(CancellationToken cancellationToken);
}
