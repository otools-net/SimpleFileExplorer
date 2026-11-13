using Microsoft.EntityFrameworkCore;
using Sfe.Core.Data;

namespace Sfe.Core.Connections;

/// <summary>Loading connections with who uses them, and what managers are shown of them.</summary>
internal static class ConnectionRows
{
    /// <summary>
    /// With their libraries and groups: what a removal needs too (no database cascade from a connection to them,
    /// agents/docs/features/connections.md).
    /// </summary>
    public static IQueryable<Connection> WithUses(this IQueryable<Connection> connections) =>
        connections.Include(c => c.Tenants).Include(c => c.Groups).ThenInclude(g => g.Group);

    /// <param name="tenantId">Only the groups of this library; null = all.</param>
    public static ConnectionInfo ToInfo(Connection connection, int? tenantId = null) =>
        new(
            connection.Id,
            connection.Kind,
            connection.Name,
            connection.Level,
            connection.OwnerId,
            connection.TenantId,
            connection.AllTenants,
            [.. connection.Tenants.Select(t => t.TenantId).Order()],
            connection.RootPath,
            connection.Account,
            connection.State,
            connection.Disabled,
            connection.ReadOnly,
            connection.Created,
            connection.CreatedById,
            [
                .. connection
                    .Groups.Where(g => g.Group is not null && (tenantId is null || g.Group.TenantId == tenantId))
                    .OrderBy(g => g.GroupId)
                    .Select(g => new ConnectionGroupInfo(g.GroupId, g.Group!.TenantId, g.Level)),
            ]
        );

    /// <summary>For the log: never the credentials, nor the provider's account.</summary>
    public static string Describe(Connection connection) =>
        $"{connection.Id} \"{connection.Name}\" ({connection.Kind}, {ConnectionLevels.Name(connection.Level)})";
}
