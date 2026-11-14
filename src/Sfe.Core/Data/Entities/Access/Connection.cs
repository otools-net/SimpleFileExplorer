using Otools.Net.Sfe.Core.Connections;

namespace Otools.Net.Sfe.Core.Data;

/// <summary>
/// A storage outside the libraries that an account signed in to (Dropbox…), browsed beside a library as a location
/// of its own. Its <see cref="Level"/> says whose it is: one account's (<see cref="OwnerId"/>), one library's
/// (<see cref="TenantId"/>), or the server's, offered to libraries (<see cref="AllTenants"/>,
/// <see cref="ConnectionTenant"/>). Who may use it then: <see cref="ConnectionGroup"/>
/// (<see cref="ConnectionAccess"/>).
/// </summary>
public sealed class Connection
{
    public int Id { get; set; }

    /// <summary>The provider's kind (<see cref="IConnectionProvider.Kind"/>).</summary>
    public string Kind { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public ConnectionLevel Level { get; set; }

    /// <summary>The account it is private to (account level only): removed with it.</summary>
    public int? OwnerId { get; set; }

    /// <summary>The library it belongs to (library level only): removed with it.</summary>
    public int? TenantId { get; set; }

    /// <summary>Server level: offered to every library, those made later too.</summary>
    public bool AllTenants { get; set; }

    /// <summary>
    /// The folder of the remote storage the connection shows as its root (<see cref="RemotePaths"/>); empty for the
    /// whole storage. Chosen when it is made.
    /// </summary>
    public string RootPath { get; set; } = string.Empty;

    /// <summary>
    /// What the provider needs to open the storage (a refresh token), protected with the server's keys
    /// (<see cref="ICredentialProtector"/>): never sent, logged or shown. Null until signed in.
    /// </summary>
    public string? Credentials { get; set; }

    /// <summary>Whose storage it is at the provider, to show (an e-mail address).</summary>
    public string? Account { get; set; }

    /// <summary>The provider's id of that account: a reconnection to another account is told apart by it.</summary>
    public string? AccountId { get; set; }

    public ConnectionState State { get; set; }

    /// <summary>Kept, with its groups and rules, but nobody uses it.</summary>
    public bool Disabled { get; set; }

    /// <summary>Nothing is changed in the storage through SFE, whatever the groups allow.</summary>
    public bool ReadOnly { get; set; }

    public DateTime Created { get; set; }

    /// <summary>The account that made it; null when it is gone.</summary>
    public int? CreatedById { get; set; }

    public List<ConnectionTenant> Tenants { get; set; } = [];
    public List<ConnectionGroup> Groups { get; set; } = [];

    /// <summary>Its folder rules, of every library (<see cref="ConnectionFolderRule"/>).</summary>
    public List<ConnectionFolderRule> Rules { get; set; } = [];
}
