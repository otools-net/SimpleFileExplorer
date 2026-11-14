using Otools.Net.Sfe.Core.Access;
using Otools.Net.Sfe.Core.Connections;
using Otools.Net.Sfe.Core.Transfers;

namespace Otools.Net.Sfe.Tests.Infrastructure;

/// <summary>
/// A person's access to a connection during a transfer, as a test sets it (<see cref="Folders"/>): everything by
/// default; null for a person who may no longer use it.
/// </summary>
public sealed class FixedJobAccess : IConnectionJobAccess
{
    /// <summary>Every right of a library, on no folder rule.</summary>
    public static IReadOnlySet<string> AllRights { get; } = RightCatalog.TenantRights.Select(r => r.Id).ToHashSet();

    public ConnectionFolders? Folders { get; set; } =
        new(FolderPolicy.Unrestricted, AllRights, StringComparison.OrdinalIgnoreCase);

    /// <summary>Everything but <paramref name="paths"/> and what is below them (folder rules hiding them).</summary>
    public static ConnectionFolders Hiding(params string[] paths)
    {
        var group = new ConnectionPolicyGroup(1, FolderLevel.Write, AllRights);
        var rules = paths.Select(path => new ConnectionPolicyRule(1, path, FolderLevel.Hidden)).ToList();
        var comparison = StringComparison.OrdinalIgnoreCase;
        var policy = ConnectionPolicies.Compile(new([group], rules), FolderLevel.Write, comparison);
        return new ConnectionFolders(policy, AllRights, comparison);
    }

    public Task<ConnectionFolders?> FoldersAsync(CancellationToken cancellationToken) => Task.FromResult(Folders);

    /// <summary>A side over <paramref name="storage"/> with this access and <paramref name="right"/>.</summary>
    public RemoteTransferSide Side(int id, IRemoteStorage storage, ProviderCapabilities capabilities, string right) =>
        new(id, storage, capabilities, this, right);
}
