using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Sfe.Core.Access;
using Sfe.Core.Data;

namespace Sfe.Core.Connections;

/// <summary>
/// Where in a connection a subject may use its library rights (agents/docs/features/connections.md, folder rules):
/// the connection's own <see cref="FolderPolicy"/>, compiled per subject and connection and cached until accounts,
/// groups, rights, connections or rules change (<see cref="AccessChanges"/>). <see cref="ConnectionAccess"/> stays
/// the gate (who uses the connection at all); this says where and what.
/// </summary>
/// <remarks>
/// The principals: each group of the subject in the library of the request with a <see cref="ConnectionGroup"/> row
/// for the connection, with its own rights and the row's level where no rule of it applies; and the account, with
/// the rights of those groups and no say without a rule of its own. A rule raises or lowers its principal's level
/// (nearest wins); <see cref="Connection.ReadOnly"/> caps every level at Read. A right counts only where the group
/// granting it has the level the right needs (so a right of one group never adds to the level of another). Server
/// administrators (library and server levels) and an account-level connection's owner: unrestricted but for the cap.
/// </remarks>
public sealed class ConnectionPolicies(IDbContextFactory<AccessDbContext> dbFactory, AccessChanges changes)
{
    /// <summary>Policies kept; past it the cache starts again (each entry is small, O(rules)).</summary>
    private const int MaxCached = 10_000;

    private static readonly HashSet<string> AllRights = [.. RightCatalog.TenantRights.Select(r => r.Id)];

    private readonly ConcurrentDictionary<PolicyKey, (long Version, FolderPolicy Policy)> _cache = new();

    private sealed record PolicyKey(ConnectionSubject Subject, int ConnectionId, StringComparison Comparison);

    /// <summary>
    /// Where <paramref name="subject"/> may use its rights in connection <paramref name="connectionId"/>, paths
    /// compared by <paramref name="comparison"/> (the provider's rule for case).
    /// </summary>
    public async Task<FolderPolicy> GetAsync(
        ConnectionSubject subject,
        int connectionId,
        StringComparison comparison,
        CancellationToken cancellationToken = default
    )
    {
        var version = changes.Version;
        var key = new PolicyKey(subject, connectionId, comparison);
        if (_cache.TryGetValue(key, out var cached) && cached.Version == version)
            return cached.Policy;

        var policy = await ComputeAsync(subject, connectionId, comparison, cancellationToken);
        if (_cache.Count >= MaxCached)
            _cache.Clear();
        _cache[key] = (version, policy);
        return policy;
    }

    /// <summary>
    /// The policy of a subject's groups and account in a library- or server-level connection; every level capped
    /// at <paramref name="cap"/>. Without a group the subject sees nothing (the groups' rows are the gate).
    /// </summary>
    public static FolderPolicy Compile(ConnectionPolicyInput input, FolderLevel cap, StringComparison comparison)
    {
        if (input.Groups.Count == 0)
            return Nothing(comparison);

        var principals = input
            .Groups.Select(group => new FolderPrincipal(
                RuleMap(input.Rules.Where(r => r.GroupId == group.GroupId), cap, comparison),
                group.Rights,
                false,
                Min(group.Level, cap)
            ))
            .ToList();
        principals.Add(
            new FolderPrincipal(
                RuleMap(input.Rules.Where(r => r.GroupId is null), cap, comparison),
                input.Groups.SelectMany(g => g.Rights).ToHashSet(StringComparer.Ordinal),
                false
            )
        );
        return FolderPolicy.Compile(principals, comparison);
    }

    /// <summary>Everything, but capped at <paramref name="cap"/>: an owner, a server administrator.</summary>
    public static FolderPolicy Whole(FolderLevel cap, StringComparison comparison) =>
        cap == FolderLevel.Write
            ? FolderPolicy.Unrestricted
            : FolderPolicy.Compile(
                [new FolderPrincipal(new Dictionary<string, FolderLevel>(), AllRights, false, cap)],
                comparison
            );

    /// <summary>Nothing anywhere: the connection doesn't exist for the subject.</summary>
    public static FolderPolicy Nothing(StringComparison comparison) =>
        FolderPolicy.Compile(
            [
                new FolderPrincipal(
                    new Dictionary<string, FolderLevel>(),
                    new HashSet<string>(),
                    false,
                    FolderLevel.Hidden
                ),
            ],
            comparison
        );

    private async Task<FolderPolicy> ComputeAsync(
        ConnectionSubject subject,
        int connectionId,
        StringComparison comparison,
        CancellationToken cancellationToken
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var connection = await db
            .Connections.AsNoTracking()
            .Where(c => c.Id == connectionId)
            .Select(c => new
            {
                c.Level,
                c.OwnerId,
                c.ReadOnly,
            })
            .FirstOrDefaultAsync(cancellationToken);
        if (connection is null || subject.UserId is not { } userId || subject.IsGuest)
            return Nothing(comparison);

        var cap = connection.ReadOnly ? FolderLevel.Read : FolderLevel.Write;
        if (connection.Level == ConnectionLevel.Account)
            return connection.OwnerId == userId ? Whole(cap, comparison) : Nothing(comparison);
        if (subject.IsServerAdmin)
            return Whole(cap, comparison);

        var input = await LoadAsync(db, (subject.TenantId, userId, connectionId), cancellationToken);
        return Compile(input, cap, comparison);
    }

    private static async Task<ConnectionPolicyInput> LoadAsync(
        AccessDbContext db,
        (int TenantId, int UserId, int ConnectionId) who,
        CancellationToken cancellationToken
    )
    {
        var memberOf = db
            .GroupMembers.Where(m => m.UserId == who.UserId && m.Group!.TenantId == who.TenantId)
            .Select(m => m.GroupId);
        var rows = await db
            .ConnectionGroups.AsNoTracking()
            .Where(g => g.ConnectionId == who.ConnectionId && memberOf.Contains(g.GroupId))
            .Select(g => new { g.GroupId, g.Level })
            .ToListAsync(cancellationToken);
        if (rows.Count == 0)
            return new ConnectionPolicyInput([], []);

        var groupIds = rows.Select(r => r.GroupId).ToList();
        var rights = await db
            .GroupRights.AsNoTracking()
            .Where(r => groupIds.Contains(r.GroupId))
            .Select(r => new { r.GroupId, r.Right })
            .ToListAsync(cancellationToken);
        var rules = await db
            .ConnectionFolderRules.AsNoTracking()
            .Where(r =>
                r.ConnectionId == who.ConnectionId
                && r.TenantId == who.TenantId
                && ((r.GroupId != null && groupIds.Contains(r.GroupId.Value)) || r.UserId == who.UserId)
            )
            .Select(r => new ConnectionPolicyRule(r.GroupId, r.Path, r.Level))
            .ToListAsync(cancellationToken);

        var groups = rows.GroupBy(r => r.GroupId)
            .Select(g => new ConnectionPolicyGroup(
                g.Key,
                g.Max(r => r.Level),
                rights.Where(r => r.GroupId == g.Key).Select(r => r.Right).ToHashSet(StringComparer.Ordinal)
            ))
            .ToList();
        return new ConnectionPolicyInput(groups, rules);
    }

    /// <summary>
    /// A principal's rules by path, as the provider compares paths, each capped; two rows for one path (concurrent
    /// changes) count as the stricter one.
    /// </summary>
    private static Dictionary<string, FolderLevel> RuleMap(
        IEnumerable<ConnectionPolicyRule> rules,
        FolderLevel cap,
        StringComparison comparison
    )
    {
        var comparer = StringComparer.FromComparison(comparison);
        return rules
            .GroupBy(r => r.Path, comparer)
            .ToDictionary(g => g.Key, g => Min(g.Min(r => r.Level), cap), comparer);
    }

    private static FolderLevel Min(FolderLevel level, FolderLevel cap) => level < cap ? level : cap;
}
