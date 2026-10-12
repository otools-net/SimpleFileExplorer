using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Sfe.Core.Data;
using Sfe.Core.Options;

namespace Sfe.Core.Access;

/// <summary>Who asks, in which library: an account, or a visitor without one (<see cref="UserId"/> null).</summary>
public sealed record AccessSubject(int TenantId, int? UserId);

/// <summary>What a subject may do in a library.</summary>
/// <param name="IsGuest">A visitor without an account or a guest account: the guest limit applies.</param>
/// <param name="IsMember">Belongs to the library (a group there, or a server administrator); visitors never do.</param>
public sealed record EffectiveAccess(IReadOnlySet<string> Rights, bool IsGuest, bool IsServerAdmin, bool IsMember)
{
    public static readonly EffectiveAccess None = new(new HashSet<string>(), true, false, false);

    public bool Has(string right) => Rights.Contains(right);
}

/// <summary>
/// The rights of a subject in a library: those its groups grant (the Guests group for visitors without an account),
/// less those the server disables (<see cref="RightOptions.Disabled"/>), those the library disables, and for guests
/// every right not <see cref="RightDefinition.GuestAllowed"/>. Server administrators have every right but the
/// disabled ones. Cached until accounts, groups or libraries change.
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
            var guestRights = await db.GroupRights.AsNoTracking()
                .Where(r => r.Group!.TenantId == tenant.Id && r.Group.BuiltIn == BuiltInGroup.Guests)
                .Select(r => r.Right)
                .ToListAsync(cancellationToken);
            return new EffectiveAccess(Allowed(guestRights, tenantDisabled, guest: true), IsGuest: true, IsServerAdmin: false, IsMember: false);
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
        var rights = await db.GroupRights.AsNoTracking()
            .Where(r => memberOf.Contains(r.GroupId))
            .Select(r => r.Right)
            .ToListAsync(cancellationToken);
        return new EffectiveAccess(Allowed(rights, tenantDisabled, user.IsGuest), user.IsGuest, false, memberOf.Count > 0);
    }

    private static HashSet<string> Allowed(IEnumerable<string> granted, HashSet<string> tenantDisabled, bool guest) =>
        granted
            .Where(r => RightCatalog.Find(r) is { Scope: RightScope.Tenant } definition && (!guest || definition.GuestAllowed))
            .Where(r => !tenantDisabled.Contains(r))
            .ToHashSet();
}
