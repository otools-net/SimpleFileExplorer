using Microsoft.EntityFrameworkCore;
using Sfe.Core.Access;
using Sfe.Core.Data;
using Sfe.Core.Options;
using Sfe.Core.Settings;

namespace Sfe.Core.Connections;

/// <summary>
/// Who may use which connection, and how far: the one place routes and jobs ask
/// (agents/docs/features/connections.md). A connection a subject may not use does not exist for it (404). The
/// route still asks <c>connections.use</c> and the <c>files.*</c> right of what it does.
/// </summary>
/// <remarks>
/// The rules: connections on, its kind and level allowed by the settings, not disabled. Account level: its owner
/// only, never an administrator (they list and remove it through the management routes, never open it). Library
/// level: the library of the request, at the best level of the subject's groups there. Server level: offered to
/// the library of the request (all, or by a <see cref="ConnectionTenant"/> row), then the groups the same way.
/// Server administrators: Write on library and server levels. <see cref="Connection.ReadOnly"/> caps at Read; guests
/// and visitors never.
/// </remarks>
public sealed class ConnectionAccess(IDbContextFactory<AccessDbContext> dbFactory, SettingLayers settings)
{
    /// <summary>How far <paramref name="subject"/> may use connection <paramref name="connectionId"/>; null = not at
    /// all.</summary>
    public async Task<FolderLevel?> LevelAsync(
        ConnectionSubject subject,
        int connectionId,
        CancellationToken cancellationToken = default
    )
    {
        var usable = await UsableAsync(subject, connectionId, cancellationToken);
        return usable.Count == 1 ? usable[0].Access : null;
    }

    /// <summary>The connections <paramref name="subject"/> may use in its library, by name.</summary>
    public Task<IReadOnlyList<UsableConnection>> UsableAsync(
        ConnectionSubject subject,
        CancellationToken cancellationToken = default
    ) => UsableAsync(subject, null, cancellationToken);

    /// <summary>
    /// How far a subject uses a connection loaded for it: <paramref name="groupLevel"/> is the best level of its
    /// groups in the library of the request (null: none of them has a row).
    /// </summary>
    public static FolderLevel? Decide(
        Connection connection,
        ConnectionSubject subject,
        FolderLevel? groupLevel,
        ConnectionOptions options
    )
    {
        if (
            subject.IsGuest
            || subject.UserId is null
            || connection.Disabled
            || !options.Enabled
            || !ConnectionLevels.IsUsable(connection.Kind, connection.Level, options)
        )
        {
            return null;
        }

        FolderLevel? level = connection.Level switch
        {
            ConnectionLevel.Account => connection.OwnerId == subject.UserId ? FolderLevel.Write : null,
            _ when subject.IsServerAdmin => FolderLevel.Write,
            _ => groupLevel,
        };
        if (level is not { } found || found == FolderLevel.Hidden)
            return null;
        return connection.ReadOnly ? FolderLevel.Read : found;
    }

    private async Task<IReadOnlyList<UsableConnection>> UsableAsync(
        ConnectionSubject subject,
        int? connectionId,
        CancellationToken cancellationToken
    )
    {
        var options = settings.ServerLevel().Connections;
        if (!options.Enabled || subject.IsGuest || subject.UserId is not { } userId)
            return [];

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var tenantId = subject.TenantId;
        var candidates = db
            .Connections.AsNoTracking()
            .Where(c =>
                (c.Level == ConnectionLevel.Account && c.OwnerId == userId)
                || (c.Level == ConnectionLevel.Library && c.TenantId == tenantId)
                || (c.Level == ConnectionLevel.Server && (c.AllTenants || c.Tenants.Any(t => t.TenantId == tenantId)))
            );
        if (connectionId is { } id)
            candidates = candidates.Where(c => c.Id == id);
        var connections = await candidates.ToListAsync(cancellationToken);
        if (connections.Count == 0)
            return [];

        var groupLevels = await GroupLevelsAsync(db, subject, userId, connections, cancellationToken);
        return
        [
            .. connections
                .Select(c => (Connection: c, Access: Decide(c, subject, groupLevels.GetValueOrDefault(c.Id), options)))
                .Where(pair => pair.Access is not null)
                .OrderBy(pair => pair.Connection.Name, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(pair => pair.Connection.Id)
                .Select(pair => new UsableConnection(
                    pair.Connection.Id,
                    pair.Connection.Kind,
                    pair.Connection.Name,
                    pair.Connection.Level,
                    pair.Connection.State,
                    pair.Access!.Value
                )),
        ];
    }

    /// <summary>The best level of the subject's groups in its library, by connection.</summary>
    private static async Task<Dictionary<int, FolderLevel?>> GroupLevelsAsync(
        AccessDbContext db,
        ConnectionSubject subject,
        int userId,
        List<Connection> connections,
        CancellationToken cancellationToken
    )
    {
        var shared = connections.Where(c => c.Level != ConnectionLevel.Account).Select(c => c.Id).ToList();
        if (shared.Count == 0 || subject.IsServerAdmin)
            return [];

        var groups = db
            .GroupMembers.Where(m => m.UserId == userId && m.Group!.TenantId == subject.TenantId)
            .Select(m => m.GroupId);
        var rows = await db
            .ConnectionGroups.AsNoTracking()
            .Where(g => shared.Contains(g.ConnectionId) && groups.Contains(g.GroupId))
            .Select(g => new { g.ConnectionId, g.Level })
            .ToListAsync(cancellationToken);
        return rows.GroupBy(r => r.ConnectionId).ToDictionary(g => g.Key, g => (FolderLevel?)g.Max(r => r.Level));
    }
}
