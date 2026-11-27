using Microsoft.EntityFrameworkCore;
using Otools.Net.Sfe.Core.Connections;
using Otools.Net.Sfe.Core.Data;

namespace Otools.Net.Sfe.Core.Access;

/// <summary>
/// What an administrator has done of the first things to set up in a space, for the getting-started checklist of the
/// administration page (agents/docs/features/getting-started.md): each item is asked only of those with the right of
/// its page, and each answer is one indexed <c>EXISTS</c>, never a count.
/// </summary>
public sealed class GettingStarted(IDbContextFactory<AccessDbContext> dbFactory)
{
    /// <summary>The items, in the checklist's order, with the rights (any of them) that show each.</summary>
    public static readonly IReadOnlyList<(string Id, string[] Rights)> Items =
    [
        (GettingStartedItems.Library, [Rights.ServerSettings]),
        (GettingStartedItems.Groups, [Rights.ManageMembers]),
        (GettingStartedItems.Folders, [Rights.ManageMembers]),
        (GettingStartedItems.Accounts, [Rights.ManageMembers, Rights.ManageUsers]),
        (GettingStartedItems.Connection, [Rights.LibraryConnections, Rights.ServerConnections]),
    ];

    /// <summary>
    /// The items <paramref name="has"/> shows, each with whether it is done in space <paramref name="tenantId"/> for
    /// account <paramref name="userId"/>; <paramref name="libraryFound"/> says whether the space's folder is there.
    /// </summary>
    public async Task<IReadOnlyList<GettingStartedItem>> ItemsAsync(
        (int TenantId, int UserId, bool LibraryFound) space,
        Func<string, bool> has,
        CancellationToken cancellationToken = default
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var items = new List<GettingStartedItem>();
        foreach (var (id, rights) in Items.Where(item => item.Rights.Any(has)))
            items.Add(new GettingStartedItem(id, await DoneAsync(db, id, space, cancellationToken)));
        return items;
    }

    private static async Task<bool> DoneAsync(
        AccessDbContext db,
        string id,
        (int TenantId, int UserId, bool LibraryFound) space,
        CancellationToken cancellationToken
    )
    {
        var (tenantId, userId, libraryFound) = space;
        return id switch
        {
            GettingStartedItems.Library => libraryFound,
            // A group made by an administrator: the built-in ones exist in every space.
            GettingStartedItems.Groups => await db.Groups.AnyAsync(
                g => g.TenantId == tenantId && g.BuiltIn == null,
                cancellationToken
            ),
            GettingStartedItems.Folders => await db.FolderRules.AnyAsync(
                r => r.TenantId == tenantId,
                cancellationToken
            ),
            // Someone else than the administrator asking can sign in to the space.
            GettingStartedItems.Accounts => await db.GroupMembers.AnyAsync(
                m => m.Group!.TenantId == tenantId && m.UserId != userId,
                cancellationToken
            ),
            // A connection of this space, one the server offers it, or the administrator's own.
            GettingStartedItems.Connection => await db.Connections.AnyAsync(
                c =>
                    (c.Level == ConnectionLevel.Account && c.OwnerId == userId)
                    || (c.Level == ConnectionLevel.Library && c.TenantId == tenantId)
                    || (
                        c.Level == ConnectionLevel.Server
                        && (c.AllTenants || c.Tenants.Any(t => t.TenantId == tenantId))
                    ),
                cancellationToken
            ),
            _ => false,
        };
    }
}
