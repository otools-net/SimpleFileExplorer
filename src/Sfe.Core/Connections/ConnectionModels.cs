using Sfe.Core.Access;

namespace Sfe.Core.Connections;

/// <summary>
/// A connection as those who manage it see it: never its credentials (<see cref="Data.Connection.Credentials"/>).
/// </summary>
/// <param name="TenantIds">Server level: the libraries it is offered to besides <paramref name="AllTenants"/>.</param>
/// <param name="Groups">Who uses it, by group: all of them, or those of one library when listed for it.</param>
public sealed record ConnectionInfo(
    int Id,
    string Kind,
    string Name,
    ConnectionLevel Level,
    int? OwnerId,
    int? TenantId,
    bool AllTenants,
    IReadOnlyList<int> TenantIds,
    string RootPath,
    string? Account,
    ConnectionState State,
    bool Disabled,
    bool ReadOnly,
    DateTime Created,
    int? CreatedById,
    IReadOnlyList<ConnectionGroupInfo> Groups
);

/// <summary>A group of library <paramref name="TenantId"/> using a connection at <paramref name="Level"/>.</summary>
public sealed record ConnectionGroupInfo(int GroupId, int TenantId, FolderLevel Level);

/// <summary>A connection to make, before its sign-in's grant is added.</summary>
/// <param name="OwnerId">Account level: whose it is.</param>
/// <param name="TenantId">Library level: whose it is.</param>
/// <param name="RootPath">The folder of the remote storage it shows (<see cref="RemotePaths"/>); empty or null for
/// all of it.</param>
public sealed record ConnectionDraft(
    string Kind,
    string? Name,
    ConnectionLevel Level,
    int? OwnerId = null,
    int? TenantId = null,
    string? RootPath = null
);

/// <summary>What a manager changes in a connection; null keeps the value.</summary>
public sealed record ConnectionChange(
    string? Name = null,
    bool? Disabled = null,
    bool? ReadOnly = null,
    string? RootPath = null
);

/// <summary>A connection signed in again.</summary>
/// <param name="OtherAccount">The provider's account is not the one it was signed in to before: whoever uses the
/// connection now sees other files.</param>
public sealed record ConnectionReauthorized(ConnectionInfo Connection, bool OtherAccount);

/// <summary>Who asks to use connections, in which library (<see cref="ConnectionAccess"/>).</summary>
/// <param name="UserId">Null for a visitor without an account: never any connection.</param>
/// <param name="IsGuest">A guest account or a visitor: never any connection.</param>
public sealed record ConnectionSubject(int TenantId, int? UserId, bool IsServerAdmin, bool IsGuest);

/// <summary>A connection a subject may use, and how far (<see cref="FolderLevel.Read"/> or Write).</summary>
public sealed record UsableConnection(
    int Id,
    string Kind,
    string Name,
    ConnectionLevel Level,
    ConnectionState State,
    FolderLevel Access
);
