using Sfe.Core.Data;

namespace Sfe.Core.Access;

/// <summary>
/// The rights of connections to other storage, a part of <see cref="RightCatalog.All"/> kept in its own file.
/// None is ever a guest's, and existing spaces get them for their Administrators only (<see cref="Since"/>): what
/// one does inside a connection still needs the <c>files.*</c> rights, narrowed by the connection's own level for
/// the person. They build their definitions without <see cref="RightCatalog"/>, which reads this list while it
/// initializes.
/// </summary>
internal static class ConnectionRights
{
    /// <summary>The catalog revision these rights came with.</summary>
    private const int Since = 15;

    private const string Area = "Connections";

    public static IReadOnlyList<RightDefinition> All { get; } =
    [
        Space(
            Rights.UseConnections,
            RightKind.Read,
            "Use connections",
            "See and open the connections to other storage (Dropbox…) one is allowed to use."
        ),
        Space(
            Rights.OwnConnections,
            RightKind.Write,
            "Connect own storage",
            "Connect storage of one's own, private to the account: nobody else sees its files."
        ),
        Space(
            Rights.LibraryConnections,
            RightKind.Write,
            "Manage the space's connections",
            "Connect storage for the space, choose which of its groups use it and the server's connections, "
                + "remove the connections of its accounts."
        ),
        new(
            Rights.ServerConnections,
            RightScope.Server,
            false,
            [],
            "Server",
            "Connections of the server",
            "Connect storage for the whole server and offer it to spaces; see and remove every connection.",
            Kind: RightKind.Write
        ),
    ];

    private static RightDefinition Space(string id, RightKind kind, string title, string description) =>
        new(id, RightScope.Tenant, false, [BuiltInGroup.Administrators], Area, title, description, Since, kind);
}
