using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Otools.Net.Sfe.Core.Access;
using Otools.Net.Sfe.Core.Data;
using Otools.Net.Sfe.Core.Logs;

namespace Otools.Net.Sfe.Core.Connections;

/// <summary>
/// The groups a new connection is given when it is made (<see cref="ConnectionDraft.Uses"/>, the add-a-connection
/// wizard): checked with the draft before anyone signs in at the provider, and added in the same save as the
/// connection. A server-level one is then offered to that library too. Later changes:
/// <see cref="ConnectionSharing"/>.
/// </summary>
public static class ConnectionFirstUses
{
    /// <summary>What is wrong with the draft's groups (field <c>groups</c>); null if nothing.</summary>
    public static async Task<(string Key, string Message)?> ErrorAsync(
        AccessDbContext db,
        ConnectionDraft draft,
        CancellationToken cancellationToken
    )
    {
        if (draft.Uses is not { Groups.Count: > 0 } uses)
            return null;
        if (draft.Level == ConnectionLevel.Account)
            return ("groups", "A connection of one account is used by its owner only: it is given to no group.");
        if (draft.Level == ConnectionLevel.Library && draft.TenantId != uses.TenantId)
            return ("groups", "Not a group of this space.");
        if (uses.Groups.Values.Any(level => level is not (FolderLevel.Read or FolderLevel.Write)))
            return ("groups", "A group uses a connection to read, or to write.");

        var ids = uses.Groups.Keys.ToList();
        var known = await db.Groups.CountAsync(
            g => g.TenantId == uses.TenantId && ids.Contains(g.Id),
            cancellationToken
        );
        return known == ids.Count ? null : ("groups", "Not a group of this space.");
    }

    /// <summary>Gives <paramref name="connection"/>, about to be added, the draft's groups (checked before).</summary>
    public static async Task AddAsync(
        AccessDbContext db,
        Connection connection,
        ConnectionDraft draft,
        CancellationToken cancellationToken
    )
    {
        if (draft.Uses is not { Groups.Count: > 0 } uses)
            return;
        var ids = uses.Groups.Keys.ToList();
        var groups = await db.Groups.Where(g => ids.Contains(g.Id)).ToListAsync(cancellationToken);
        connection.Groups.AddRange(
            groups.Select(group => new ConnectionGroup
            {
                GroupId = group.Id,
                Group = group,
                Level = uses.Groups[group.Id],
            })
        );
        if (draft.Level == ConnectionLevel.Server)
            connection.Tenants.Add(new ConnectionTenant { TenantId = uses.TenantId });
    }

    /// <summary>The groups given at once, as a security warning (opening a connection to more people).</summary>
    public static void Log(ILogger logger, Connection connection, ConnectionDraft draft)
    {
        if (draft.Uses is not { Groups.Count: > 0 } uses)
            return;
        logger.LogSecurityWarning(
            "Connection {Connection} given when made to groups of library {TenantId} [{Groups}]",
            ConnectionRows.Describe(connection),
            uses.TenantId,
            string.Join(", ", uses.Groups.OrderBy(p => p.Key).Select(p => $"{p.Key}: {p.Value}"))
        );
    }
}
