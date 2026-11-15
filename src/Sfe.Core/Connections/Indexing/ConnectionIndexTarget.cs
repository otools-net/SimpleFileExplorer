namespace Otools.Net.Sfe.Core.Connections.Indexing;

/// <summary>A connection as its index sees it (<see cref="ConnectionIndexer"/>).</summary>
/// <param name="RootPath">Its root folder at the provider: another one drops the rows.</param>
/// <param name="AccountKey">A hash of the provider's account: another
/// one drops the rows.</param>
/// <param name="Private">An account's own connection: its paths never go into the run's log, which the library's
/// administrators read.</param>
public sealed record ConnectionIndexTarget(
    int ConnectionId,
    string RootPath,
    string? AccountKey,
    ProviderCapabilities Capabilities,
    bool Private
)
{
    public bool CaseInsensitive => Capabilities.CaseInsensitiveNames;

    /// <summary>The connection as its index is made from it now.</summary>
    public static ConnectionIndexTarget Of(ConnectionInfo info, ProviderCapabilities capabilities) =>
        new(
            info.Id,
            info.RootPath,
            ConnectionIndexJob.AccountKeyOf(info.Account),
            capabilities,
            Private: info.Level == ConnectionLevel.Account
        );
}
