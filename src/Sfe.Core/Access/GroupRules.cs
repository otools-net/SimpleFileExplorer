using Microsoft.EntityFrameworkCore;
using Otools.Net.Sfe.Core.Data;

namespace Otools.Net.Sfe.Core.Access;

/// <summary>
/// The rules naming a group, deleted before the group: the database doesn't cascade from a group to them (two
/// cascade paths from <c>Tenants</c> on SQL Server, <see cref="AccessDbContext"/>).
/// </summary>
internal static class GroupRules
{
    /// <summary>Its folder rules in the library and in connections; inside the caller's transaction.</summary>
    public static async Task DeleteAsync(AccessDbContext db, int groupId, CancellationToken cancellationToken)
    {
        await db.FolderRules.Where(r => r.GroupId == groupId).ExecuteDeleteAsync(cancellationToken);
        await db.ConnectionFolderRules.Where(r => r.GroupId == groupId).ExecuteDeleteAsync(cancellationToken);
    }
}
