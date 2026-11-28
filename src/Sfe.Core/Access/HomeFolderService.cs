using Microsoft.EntityFrameworkCore;
using Otools.Net.Sfe.Core.Data;
using Otools.Net.Sfe.Core.Paths;

namespace Otools.Net.Sfe.Core.Access;

/// <summary>
/// The home folder of each account in each library: the one it chose (<see cref="HomeFolder"/>), else the default of
/// its groups (<see cref="UserGroup.HomeFolder"/>). Never created: whether it exists is the caller's business, which
/// knows the library (agents/docs/features/home-folder.md).
/// </summary>
public sealed class HomeFolderService(IDbContextFactory<AccessDbContext> dbFactory)
{
    /// <summary>Stands for the account's name in a group's default (e.g. <c>Homes/{user}</c>).</summary>
    public const string UserPlaceholder = "{user}";

    public async Task<HomeFolderChoice> GetAsync(
        int userId,
        int tenantId,
        CancellationToken cancellationToken = default
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var own = await db
            .HomeFolders.Where(h => h.UserId == userId && h.TenantId == tenantId)
            .Select(h => h.Path)
            .FirstOrDefaultAsync(cancellationToken);
        return new HomeFolderChoice(own, await GroupDefaultAsync(db, userId, tenantId, cancellationToken));
    }

    /// <summary>The account's own choice (root-relative, normalised by the caller), over its groups' default.</summary>
    public async Task<HomeFolderChoice> SetAsync(
        int userId,
        int tenantId,
        string path,
        CancellationToken cancellationToken = default
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var row = await db.HomeFolders.FindAsync([userId, tenantId], cancellationToken);
        if (row is null)
            db.HomeFolders.Add(
                new HomeFolder
                {
                    UserId = userId,
                    TenantId = tenantId,
                    Path = path,
                }
            );
        else
            row.Path = path;
        await db.SaveChangesAsync(cancellationToken);
        return new HomeFolderChoice(path, await GroupDefaultAsync(db, userId, tenantId, cancellationToken));
    }

    /// <summary>Forgets the account's own choice: its groups' default (if any) is its home again.</summary>
    public async Task<HomeFolderChoice> ClearAsync(
        int userId,
        int tenantId,
        CancellationToken cancellationToken = default
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await db
            .HomeFolders.Where(h => h.UserId == userId && h.TenantId == tenantId)
            .ExecuteDeleteAsync(cancellationToken);
        return new HomeFolderChoice(null, await GroupDefaultAsync(db, userId, tenantId, cancellationToken));
    }

    /// <summary>
    /// The accounts' choices and the groups' defaults naming <paramref name="from"/> or what is below it follow it to
    /// <paramref name="to"/> (a folder renamed or moved in the library; root-relative paths). A default's part after
    /// <see cref="UserPlaceholder"/> is not one folder: only what comes before it can follow.
    /// </summary>
    /// <returns>Choices and defaults changed.</returns>
    public async Task<int> MovePathsAsync(
        int tenantId,
        string from,
        string to,
        CancellationToken cancellationToken = default
    )
    {
        if (from.Length == 0)
            return 0;
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        // One row per account of the library: compared here as the host's file system does.
        var chosen = (await db.HomeFolders.Where(h => h.TenantId == tenantId).ToListAsync(cancellationToken))
            .Where(h => FolderPolicy.IsSameOrBelow(h.Path, from))
            .ToList();
        foreach (var row in chosen)
            row.Path = to + row.Path[from.Length..];
        var defaults = (
            await db.Groups.Where(g => g.TenantId == tenantId && g.HomeFolder != null).ToListAsync(cancellationToken)
        )
            .Where(g => FolderPolicy.IsSameOrBelow(g.HomeFolder!, from))
            .ToList();
        foreach (var group in defaults)
            group.HomeFolder = to + group.HomeFolder![from.Length..];
        await db.SaveChangesAsync(cancellationToken);
        return chosen.Count + defaults.Count;
    }

    /// <summary>
    /// A group's default as typed, cleaned (separators, slashes); empty = none; null when it can never be a folder of
    /// the library ('.', '..', too long).
    /// </summary>
    public static string? CleanDefault(string typed)
    {
        var path = LibraryPaths.NormalizeRelative(typed.Trim());
        return path is { Length: <= SfeDbContext.MaxPathLength } ? path : null;
    }

    /// <summary>
    /// <paramref name="template"/> for the account <paramref name="userName"/>: separators in the name are written
    /// <c>_</c>, so that it is one folder. Null when the result is not a path of the library.
    /// </summary>
    public static string? Expand(string template, string userName)
    {
        var name = userName.Replace('/', '_').Replace('\\', '_');
        return LibraryPaths.NormalizeRelative(
            template.Replace(UserPlaceholder, name, StringComparison.OrdinalIgnoreCase)
        );
    }

    /// <summary>
    /// The default of the account's groups in the library that have one: groups made by hand before the built-in ones
    /// (they are the more particular), then by name.
    /// </summary>
    private static async Task<GroupHomeFolder?> GroupDefaultAsync(
        AccessDbContext db,
        int userId,
        int tenantId,
        CancellationToken cancellationToken
    )
    {
        var groups = await db
            .GroupMembers.Where(m => m.UserId == userId && m.Group!.TenantId == tenantId && m.Group.HomeFolder != null)
            .Select(m => new
            {
                m.Group!.Name,
                m.Group.BuiltIn,
                Template = m.Group.HomeFolder!,
                m.User!.UserName,
            })
            .ToListAsync(cancellationToken);
        var first = groups
            .OrderBy(g => g.BuiltIn is not null)
            .ThenBy(g => g.BuiltIn)
            .ThenBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        return first is null
            ? null
            : new GroupHomeFolder(first.Name, first.Template, Expand(first.Template, first.UserName));
    }
}
