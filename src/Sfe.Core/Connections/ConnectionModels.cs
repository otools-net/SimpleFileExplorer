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

/// <summary>What a person asks to connect (<see cref="ConnectionSignInFlow.BeginAsync"/>).</summary>
/// <param name="Method">One of the kind's <see cref="IConnectionProvider.SignInMethods"/>.</param>
/// <param name="Fields">The method's fields as filled, by id.</param>
public sealed record ConnectRequest(
    string Kind,
    ConnectionLevel Level,
    string? Name,
    SignInKind Method,
    string? RootPath = null,
    IReadOnlyDictionary<string, string>? Fields = null
);

/// <summary>Who signs in, and where it goes back to (<see cref="ConnectionSignInFlow"/>).</summary>
/// <param name="TenantId">The library of the request: a library-level connection is that library's.</param>
/// <param name="ReturnAddress">Where the provider sends the browser back (redirect flow).</param>
public sealed record SignInContext(int UserId, int TenantId, string ReturnAddress);

/// <summary>A sign-in waiting for the person (<see cref="PendingSignIns"/>).</summary>
/// <param name="Pending">The provider's <see cref="SignInStep.Pending"/>: server side only.</param>
public sealed record PendingSignIn(
    string State,
    int UserId,
    ConnectionDraft Draft,
    SignInKind Method,
    string? Pending,
    string? ReturnAddress,
    DateTimeOffset Expires = default
);

/// <summary>A sign-in begun: done (<paramref name="Connection"/>), or what the person does next.</summary>
/// <param name="Address">Where the browser goes (the provider's page).</param>
/// <param name="State">Brought back with the code (code flow: sent with it to complete).</param>
/// <param name="Fields">What the person brings back (the code).</param>
public sealed record SignInBegun(
    ConnectionInfo? Connection,
    string? Address = null,
    string? State = null,
    IReadOnlyList<SignInField>? Fields = null
);
