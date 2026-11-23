using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Otools.Net.Sfe.Core.Access;
using Otools.Net.Sfe.Core.Data;
using Otools.Net.Sfe.Core.Logs;

namespace Otools.Net.Sfe.Core.Connections;

/// <summary>
/// Who uses a library- or server-level connection: the libraries a server-level one is offered to, and in each
/// library the groups that use it (Read or Write). Opening a connection to more people is a security event of the
/// log. Checks the rows mean something (a library that exists, a group of that library) but not who asks.
/// </summary>
public sealed class ConnectionSharing(
    IDbContextFactory<AccessDbContext> dbFactory,
    AccessChanges changes,
    ILogger<ConnectionSharing> logger
)
{
    /// <summary>
    /// Offers a server-level connection to every library (<paramref name="allTenants"/>, those made later too) or to
    /// <paramref name="tenantIds"/>. A library no longer offered loses its groups' rows and its folder rules.
    /// </summary>
    public async Task<AccessResult<ConnectionInfo>> SetTenantsAsync(
        int id,
        bool allTenants,
        IReadOnlyCollection<int> tenantIds,
        int? changedById,
        CancellationToken cancellationToken = default
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var connection = await db
            .Connections.WithUsesAndRules()
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (connection is null)
            return AccessResult<ConnectionInfo>.Missing();
        if (connection.Level != ConnectionLevel.Server)
        {
            return AccessResult<ConnectionInfo>.Invalid(
                "level",
                "Only a connection of the whole server is offered to spaces."
            );
        }

        var wanted = tenantIds.ToHashSet();
        var known = await db.Tenants.Where(t => wanted.Contains(t.Id)).Select(t => t.Id).ToListAsync(cancellationToken);
        if (known.Count != wanted.Count)
            return AccessResult<ConnectionInfo>.Invalid("tenantIds", "No such space.");

        var before = connection.Tenants.Select(t => t.TenantId).ToHashSet();
        var openedToMore =
            (allTenants && !connection.AllTenants) || (!connection.AllTenants && !wanted.IsSubsetOf(before));
        connection.AllTenants = allTenants;
        connection.Tenants.RemoveAll(t => !wanted.Contains(t.TenantId));
        connection.Tenants.AddRange(wanted.Except(before).Select(t => new ConnectionTenant { TenantId = t }));
        if (!allTenants)
        {
            connection.Groups.RemoveAll(g => !wanted.Contains(g.Group!.TenantId));
            connection.Rules.RemoveAll(r => !wanted.Contains(r.TenantId));
        }
        await db.SaveChangesAsync(cancellationToken);
        changes.Changed();

        var libraries = allTenants ? "every library" : $"libraries [{string.Join(", ", wanted.Order())}]";
        if (openedToMore)
        {
            logger.LogSecurityWarning(
                "Connection {Connection} offered to more by account {UserId}: {Libraries}",
                ConnectionRows.Describe(connection),
                changedById,
                libraries
            );
        }
        else
        {
            logger.LogInformation(
                "Connection {Connection} offered by account {UserId} to {Libraries}",
                ConnectionRows.Describe(connection),
                changedById,
                libraries
            );
        }
        return AccessResult<ConnectionInfo>.Ok(ConnectionRows.ToInfo(connection));
    }

    /// <summary>
    /// The groups of library <paramref name="tenantId"/> that use a connection reachable there (its own, or the
    /// server's offered to it), replacing those it had; groups of other libraries are left as they are.
    /// </summary>
    public async Task<AccessResult<ConnectionInfo>> SetGroupsAsync(
        int id,
        int tenantId,
        IReadOnlyDictionary<int, FolderLevel> levels,
        int? changedById,
        CancellationToken cancellationToken = default
    )
    {
        if (levels.Values.Any(level => level is not (FolderLevel.Read or FolderLevel.Write)))
            return AccessResult<ConnectionInfo>.Invalid("level", "A group uses a connection to read, or to write.");

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var connection = await db.Connections.WithUses().FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (connection is null || !IsReachable(connection, tenantId))
            return AccessResult<ConnectionInfo>.Missing();

        var ids = levels.Keys.ToList();
        var groups = await db
            .Groups.Where(g => g.TenantId == tenantId && ids.Contains(g.Id))
            .ToDictionaryAsync(g => g.Id, cancellationToken);
        if (groups.Count != ids.Count)
            return AccessResult<ConnectionInfo>.Invalid("groupId", "Not a group of this space.");

        var before = connection
            .Groups.Where(g => g.Group!.TenantId == tenantId)
            .ToDictionary(g => g.GroupId, g => g.Level);
        var openedToMore = levels.Any(pair => !before.TryGetValue(pair.Key, out var was) || was < pair.Value);
        // Rows kept are changed in place: one removed and added again under the same key would be tracked twice.
        connection.Groups.RemoveAll(g => g.Group!.TenantId == tenantId && !levels.ContainsKey(g.GroupId));
        foreach (var row in connection.Groups.Where(g => levels.ContainsKey(g.GroupId)))
            row.Level = levels[row.GroupId];
        connection.Groups.AddRange(
            levels
                .Where(pair => !before.ContainsKey(pair.Key))
                .Select(pair => new ConnectionGroup
                {
                    GroupId = pair.Key,
                    Group = groups[pair.Key],
                    Level = pair.Value,
                })
        );
        await db.SaveChangesAsync(cancellationToken);
        changes.Changed();

        var described = string.Join(", ", levels.OrderBy(p => p.Key).Select(p => $"{p.Key}: {p.Value}"));
        if (openedToMore)
        {
            logger.LogSecurityWarning(
                "Connection {Connection} opened to more by account {UserId}: groups of library {TenantId} [{Groups}]",
                ConnectionRows.Describe(connection),
                changedById,
                tenantId,
                described
            );
        }
        else
        {
            logger.LogInformation(
                "Connection {Connection} given by account {UserId} to groups of library {TenantId} [{Groups}]",
                ConnectionRows.Describe(connection),
                changedById,
                tenantId,
                described
            );
        }
        return AccessResult<ConnectionInfo>.Ok(ConnectionRows.ToInfo(connection, tenantId));
    }

    /// <summary>The connection is one library <paramref name="tenantId"/> manages groups of.</summary>
    public static bool IsReachable(Connection connection, int tenantId) =>
        connection.Level switch
        {
            ConnectionLevel.Library => connection.TenantId == tenantId,
            ConnectionLevel.Server => connection.AllTenants || connection.Tenants.Any(t => t.TenantId == tenantId),
            _ => false,
        };
}
