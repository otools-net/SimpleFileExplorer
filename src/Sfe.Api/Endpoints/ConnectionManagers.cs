using Sfe.Api.Security;
using Sfe.Core.Access;
using Sfe.Core.Connections;
using ConnectionInfo = Sfe.Core.Connections.ConnectionInfo;

namespace Sfe.Api.Endpoints;

/// <summary>
/// Who manages a connection (makes it, signs it in again, renames, switches, removes it): the one rule of the
/// sign-in and management routes. Using one is <see cref="ConnectionAccess"/>'s.
/// </summary>
public static class ConnectionManagers
{
    /// <summary>The rights, one of which lets a session manage some connection.</summary>
    public static readonly string[] Rights =
    [
        Core.Access.Rights.OwnConnections,
        Core.Access.Rights.LibraryConnections,
        Core.Access.Rights.ServerConnections,
    ];

    /// <summary>
    /// Account level: its owner with <c>connections.own</c> (never an administrator for someone else); library level:
    /// that library, the request's, with <c>connections.library</c>; server level: <c>connections.server</c>.
    /// </summary>
    public static bool MayManage(AccessState state, ConnectionDraft draft) =>
        state.User is { } user
        && draft.Level switch
        {
            ConnectionLevel.Account => draft.OwnerId == user.Id && state.Has(Core.Access.Rights.OwnConnections),
            ConnectionLevel.Library => draft.TenantId == state.Tenant?.Id
                && state.Has(Core.Access.Rights.LibraryConnections),
            ConnectionLevel.Server => state.Has(Core.Access.Rights.ServerConnections),
            _ => false,
        };

    public static bool MayManage(AccessState state, ConnectionInfo connection) => MayManage(state, DraftOf(connection));

    /// <summary>
    /// Removing it: who manages it, and the server's administrators for an account's connection (they see that it
    /// exists and can remove it, never open it).
    /// </summary>
    public static bool MayRemove(AccessState state, ConnectionInfo connection) =>
        MayManage(state, connection)
        || (connection.Level == ConnectionLevel.Account && state.User is { IsServerAdmin: true });

    public static ConnectionDraft DraftOf(ConnectionInfo connection) =>
        new(
            connection.Kind,
            connection.Name,
            connection.Level,
            connection.OwnerId,
            connection.TenantId,
            connection.RootPath
        );
}
