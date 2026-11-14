using Sfe.Api.Security;
using Sfe.Core.Access;
using Sfe.Core.Connections;
using ConnectionInfo = Sfe.Core.Connections.ConnectionInfo;

namespace Sfe.Api.Endpoints;

/// <summary>
/// What the routes of a connection's folder rules use (bound with <c>[AsParameters]</c>; none of it is in the
/// OpenAPI document), and the one way they find the connection: one the request's library reaches (its own, or the
/// server's offered to it), else none.
/// </summary>
internal sealed record ConnectionFolderRuleServices(
    CurrentAccess Current,
    ConnectionService Connections,
    ConnectionRuleService Rules,
    AccessService Accounts,
    HttpContext Context
)
{
    /// <summary>The connection and the request's library; null when the library doesn't reach it.</summary>
    public async Task<(ConnectionInfo Connection, AccessState State)?> ReachedAsync(
        int id,
        CancellationToken cancellationToken
    )
    {
        var state = await Current.GetAsync();
        if (state.Tenant is not { } tenant)
            return null;
        var connection = await Connections.FindAsync(id, cancellationToken);
        return connection is not null && Reaches(connection, tenant.Id) ? (connection, state) : null;
    }

    /// <summary>The library's rules in the connection, named after their group or account.</summary>
    public async Task<IReadOnlyList<ConnectionFolderRuleView>> ViewsAsync(
        int connectionId,
        int tenantId,
        CancellationToken cancellationToken
    ) =>
        await ViewsOfAsync(
            await Rules.GetAsync(connectionId, tenantId, cancellationToken),
            tenantId,
            cancellationToken
        );

    /// <inheritdoc cref="ViewsAsync"/>
    public async Task<IReadOnlyList<ConnectionFolderRuleView>> ViewsOfAsync(
        IReadOnlyList<ConnectionRuleInfo> rules,
        int tenantId,
        CancellationToken cancellationToken
    )
    {
        var groups = (await Accounts.GetGroupsAsync(tenantId, cancellationToken)).ToDictionary(g => g.Id, g => g.Name);
        var users = (await Accounts.GetUsersAsync(tenantId, cancellationToken)).ToDictionary(
            u => u.Id,
            u => u.DisplayName ?? u.UserName
        );
        return
        [
            .. rules.Select(r => new ConnectionFolderRuleView(
                r.Id,
                r.Path,
                r.GroupId,
                r.UserId,
                r.GroupId is { } g
                    ? groups.GetValueOrDefault(g, $"Group {g}")
                    : users.GetValueOrDefault(r.UserId!.Value, $"Account {r.UserId}"),
                r.Level,
                r.ItemId
            )),
        ];
    }

    /// <summary>As <see cref="ConnectionSharing.IsReachable"/>, on what a manager sees of the connection.</summary>
    private static bool Reaches(ConnectionInfo connection, int tenantId) =>
        connection.Level switch
        {
            ConnectionLevel.Library => connection.TenantId == tenantId,
            ConnectionLevel.Server => connection.AllTenants || connection.TenantIds.Contains(tenantId),
            _ => false,
        };
}
