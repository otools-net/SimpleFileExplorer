using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Sfe.Core.Data;
using Sfe.Core.Options;
using Sfe.Core.Paths;

namespace Sfe.Core.Access;

/// <summary>Who asks, in which library: an account, or a visitor without one (<see cref="UserId"/> null).</summary>
public sealed record AccessSubject(int TenantId, int? UserId);

/// <summary>What a subject may do in a library.</summary>
/// <param name="IsGuest">A visitor without an account or a guest account: the guest limit applies.</param>
/// <param name="IsMember">Belongs to the library (a group there, or a server administrator); visitors never do.</param>
/// <param name="Folders">Where it may use its rights (folder rules); null = everywhere.</param>
public sealed record EffectiveAccess(IReadOnlySet<string> Rights, bool IsGuest, bool IsServerAdmin, bool IsMember, FolderPolicy? Folders = null)
{
    public static readonly EffectiveAccess None = new(new HashSet<string>(), true, false, false);

    /// <summary>Has the right somewhere in the library (what <c>RequireRight</c> checks).</summary>
    public bool Has(string right) => Rights.Contains(right);

    public FolderPolicy FolderPolicy => Folders ?? FolderPolicy.Unrestricted;

    /// <summary>What it may do at a root-relative, canonical path.</summary>
    public FolderAccess At(string path) => FolderPolicy.At(path, Rights);

    /// <summary>Where it may use <paramref name="right"/>, for the queries over the index.</summary>
    public PathFilter FilterFor(string right) => FolderPolicy.FilterFor(right, Rights);
}

/// <summary>
/// The rights of a subject in a library: those its groups grant (the Guests group for visitors without an account),
/// less those the server disables (<see cref="RightOptions.Disabled"/>), those the library disables, and for guests
/// every right not <see cref="RightDefinition.GuestAllowed"/>; and where it may use them, from the folder rules of its
/// groups and account (<see cref="FolderPolicy"/>). Server administrators have every right everywhere but the
/// disabled ones. Cached until accounts, groups, folder rules or libraries change.
/// </summary>
public sealed class RightsEvaluator(
    IDbContextFactory<AccessDbContext> dbFactory,
    AccessChanges changes,
    AccessService accounts,
    IOptionsMonitor<RightOptions> rightOptions,
    IOptionsMonitor<SecurityOptions> security)
{
    private readonly ConcurrentDictionary<AccessSubject, (long Version, EffectiveAccess Access)> _cache = new();

    public async Task<EffectiveAccess> GetAsync(AccessSubject subject, CancellationToken cancellationToken = default)
    {
        var version = changes.Version;
        if (_cache.TryGetValue(subject, out var cached) && cached.Version == version)
            return Filter(cached.Access);

        var access = await ComputeAsync(subject, cancellationToken);
        _cache[subject] = (version, access);
        return Filter(access);
    }

    /// <summary>The libraries an account reaches (server administrators: every enabled one), first library first.</summary>
    public async Task<IReadOnlyList<int>> GetTenantIdsAsync(int userId, CancellationToken cancellationToken = default)
    {
        var user = await accounts.GetSessionUserAsync(userId, cancellationToken);
        if (user is null || user.Disabled)
            return [];

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var tenants = db.Tenants.AsNoTracking().Where(t => !t.Disabled);
        if (!user.IsServerAdmin)
            tenants = tenants.Where(t => t.Groups.Any(g => g.Members.Any(m => m.UserId == userId)));
        return await tenants.OrderByDescending(t => t.IsDefault).ThenBy(t => t.Name).Select(t => t.Id).ToListAsync(cancellationToken);
    }

    /// <summary>The server's switches apply at once, also to cached entries (they come from the configuration).</summary>
    private EffectiveAccess Filter(EffectiveAccess access)
    {
        var disabled = rightOptions.CurrentValue.Disabled;
        var anonymousOff = !security.CurrentValue.AnonymousGuests;
        if (disabled.Count == 0 && !anonymousOff)
            return access;
        if (anonymousOff && access is { IsGuest: true, IsMember: false })
            return EffectiveAccess.None;
        return access with { Rights = access.Rights.Where(r => !RightCatalog.MatchesAny(disabled, r)).ToHashSet() };
    }

    private async Task<EffectiveAccess> ComputeAsync(AccessSubject subject, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var tenant = await db.Tenants.AsNoTracking().Include(t => t.DisabledRights).FirstOrDefaultAsync(t => t.Id == subject.TenantId, cancellationToken);
        if (tenant is null || tenant.Disabled)
            return EffectiveAccess.None;
        var tenantDisabled = tenant.DisabledRights.Select(r => r.Right).ToHashSet();

        if (subject.UserId is not { } userId)
        {
            if (!tenant.GuestAccess)
                return EffectiveAccess.None;
            var guests = await db.Groups.AsNoTracking()
                .Where(g => g.TenantId == tenant.Id && g.BuiltIn == BuiltInGroup.Guests)
                .Select(g => g.Id)
                .ToListAsync(cancellationToken);
            var guestRights = await GroupRightsAsync(db, guests, tenantDisabled, guest: true, cancellationToken);
            var guestFolders = await FoldersAsync(db, tenant.Id, guestRights, userId: null, cancellationToken);
            return new EffectiveAccess(Union(guestRights), IsGuest: true, IsServerAdmin: false, IsMember: false, guestFolders);
        }

        var user = await accounts.GetSessionUserAsync(userId, cancellationToken);
        if (user is null || user.Disabled)
            return EffectiveAccess.None;
        if (user.IsServerAdmin)
            return new EffectiveAccess(RightCatalog.All.Select(r => r.Id).Where(r => !tenantDisabled.Contains(r)).ToHashSet(), false, true, true);

        var memberOf = await db.GroupMembers.AsNoTracking()
            .Where(m => m.UserId == userId && m.Group!.TenantId == tenant.Id)
            .Select(m => m.GroupId)
            .ToListAsync(cancellationToken);
        var rights = await GroupRightsAsync(db, memberOf, tenantDisabled, user.IsGuest, cancellationToken);
        var folders = await FoldersAsync(db, tenant.Id, rights, userId, cancellationToken);
        return new EffectiveAccess(Union(rights), user.IsGuest, false, memberOf.Count > 0, folders);
    }

    /// <summary>The rights each group grants, those the library or the guest limit takes away left out.</summary>
    private static async Task<Dictionary<int, HashSet<string>>> GroupRightsAsync(
        AccessDbContext db, IReadOnlyCollection<int> groupIds, HashSet<string> tenantDisabled, bool guest, CancellationToken cancellationToken)
    {
        var rows = await db.GroupRights.AsNoTracking()
            .Where(r => groupIds.Contains(r.GroupId))
            .Select(r => new { r.GroupId, r.Right })
            .ToListAsync(cancellationToken);
        return groupIds.ToDictionary(id => id, id => Allowed(rows.Where(r => r.GroupId == id).Select(r => r.Right), tenantDisabled, guest));
    }

    /// <summary>
    /// The folder rules of the groups and of the account, compiled: each group with its own rights (rights stay with
    /// the group that grants them), the account with all of them.
    /// </summary>
    private static async Task<FolderPolicy?> FoldersAsync(
        AccessDbContext db, int tenantId, Dictionary<int, HashSet<string>> groupRights, int? userId, CancellationToken cancellationToken)
    {
        var groupIds = groupRights.Keys.ToList();
        var rules = await db.FolderRules.AsNoTracking()
            .Where(r => r.TenantId == tenantId && ((r.GroupId != null && groupIds.Contains(r.GroupId.Value)) || (userId != null && r.UserId == userId)))
            .Select(r => new { r.GroupId, r.UserId, r.Path, r.Level })
            .ToListAsync(cancellationToken);
        if (rules.Count == 0)
            return null;

        var principals = groupRights
            .Select(g => new FolderPrincipal(RuleMap(rules.Where(r => r.GroupId == g.Key).Select(r => (r.Path, r.Level))), g.Value, true))
            .Append(new FolderPrincipal(RuleMap(rules.Where(r => r.UserId is not null).Select(r => (r.Path, r.Level))), Union(groupRights), false))
            .ToList();
        return FolderPolicy.Compile(principals);
    }

    /// <summary>A principal's rules by path; two rows for one path (concurrent changes) count as the stricter one.</summary>
    public static Dictionary<string, FolderLevel> RuleMap(IEnumerable<(string Path, FolderLevel Level)> rules) =>
        rules.GroupBy(r => r.Path, LibraryPaths.PathComparer).ToDictionary(g => g.Key, g => g.Min(r => r.Level), LibraryPaths.PathComparer);

    private static HashSet<string> Union(Dictionary<int, HashSet<string>> groupRights) => groupRights.Values.SelectMany(r => r).ToHashSet();

    private static HashSet<string> Allowed(IEnumerable<string> granted, HashSet<string> tenantDisabled, bool guest) =>
        granted
            .Where(r => RightCatalog.Find(r) is { Scope: RightScope.Tenant } definition && (!guest || definition.GuestAllowed))
            .Where(r => !tenantDisabled.Contains(r))
            .ToHashSet();
}
