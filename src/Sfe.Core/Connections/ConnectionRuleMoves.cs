using Microsoft.EntityFrameworkCore;
using Sfe.Core.Access;
using Sfe.Core.Data;

namespace Sfe.Core.Connections;

/// <summary>
/// The folder rules of a connection following a folder renamed or moved through SFE, for every library, in the
/// library's order (agents/docs/development/file-operations.md): <see cref="BeginAsync"/> copies the rules to the new
/// paths, the provider moves the folder, then <see cref="ConnectionRuleMove"/> completes (the old ones go) or aborts
/// (the copies go). What the move changes for the asking library's people: <see cref="CompareAsync"/>.
/// </summary>
/// <param name="gate">The connection's lock, shared with the other rule changes.</param>
internal sealed class ConnectionRuleMoves(
    IDbContextFactory<AccessDbContext> dbFactory,
    AccessChanges changes,
    Func<int, SemaphoreSlim> gate
)
{
    /// <summary>See <see cref="ConnectionRuleService.CompareMoveAsync"/>.</summary>
    public async Task<IReadOnlyList<FolderLevelChange>> CompareAsync(
        ConnectionMovePlan plan,
        CancellationToken cancellationToken
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var rules = await db
            .ConnectionFolderRules.AsNoTracking()
            .Where(r => r.ConnectionId == plan.ConnectionId && r.TenantId == plan.TenantId)
            .ToListAsync(cancellationToken);
        var (groups, cap) = await DefaultsAsync(db, plan, cancellationToken);
        return ConnectionMoveComparison.Compare(rules, groups, cap, plan);
    }

    /// <summary>See <see cref="ConnectionRuleService.BeginMoveAsync"/>; the paths already normalised.</summary>
    public async Task<AccessResult<ConnectionRuleMove>> BeginAsync(
        ConnectionMovePlan plan,
        bool keepAccess,
        int? changedById,
        CancellationToken cancellationToken
    )
    {
        var lockOfConnection = gate(plan.ConnectionId);
        await lockOfConnection.WaitAsync(cancellationToken);
        var handed = false;
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            var rules = await db
                .ConnectionFolderRules.Where(r => r.ConnectionId == plan.ConnectionId)
                .ToListAsync(cancellationToken);
            var moving = rules.Where(r => RemotePaths.IsInTree(r.Path, plan.Source, plan.Comparison)).ToList();
            var caseOnly = string.Equals(plan.Source, plan.Target, plan.Comparison);
            // Rules where the folder goes are left from a folder that is gone: they would apply to the moved one.
            List<ConnectionFolderRule> left = caseOnly
                ? []
                :
                [
                    .. rules.Where(r =>
                        !moving.Contains(r) && RemotePaths.IsInTree(r.Path, plan.Target, plan.Comparison)
                    ),
                ];
            if (Refusal(plan, moving, left) is { } refused)
                return refused;

            var pins = keepAccess && !caseOnly ? await PinsAsync(db, plan, rules, changedById, cancellationToken) : [];
            var rows = caseOnly
                ? await RenameInPlaceAsync(db, plan, moving, cancellationToken)
                : await CopyAsync(db, plan, (moving, pins, left), cancellationToken);
            if (rows.AtTarget.Count > 0)
                changes.Changed();
            handed = true;
            return AccessResult<ConnectionRuleMove>.Ok(new ConnectionRuleMove(this, lockOfConnection, rows));
        }
        finally
        {
            if (!handed)
                lockOfConnection.Release();
        }
    }

    /// <summary>
    /// The folder moved: the copies take the provider's path for it (another name when it numbered it) and its id,
    /// the old rules and those left stale where it went go.
    /// </summary>
    internal async Task CompleteAsync(
        ConnectionRuleMoveRows rows,
        RemoteItem? moved,
        CancellationToken cancellationToken
    )
    {
        var plan = rows.Plan;
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var placed = rows.AtTarget.ToList();
        var target = RemotePaths.Normalize(moved?.Path) is { Length: > 0 } where ? where : plan.Target;
        foreach (
            var rule in await db.ConnectionFolderRules.Where(r => placed.Contains(r.Id)).ToListAsync(cancellationToken)
        )
        {
            var path = target + rule.Path[plan.Target.Length..];
            if (!string.Equals(target, plan.Target, plan.Comparison) && path.Length <= RemotePaths.MaxPathLength)
                rule.Path = path;
            if (string.Equals(rule.Path, target, plan.Comparison) && IdOf(moved) is { } id)
                rule.ItemId = id;
        }
        await db.SaveChangesAsync(cancellationToken);

        var gone = rows.Originals.Concat(rows.Stale).ToList();
        if (gone.Count > 0)
            await db.ConnectionFolderRules.Where(r => gone.Contains(r.Id)).ExecuteDeleteAsync(cancellationToken);
        if (gone.Count > 0 || placed.Count > 0)
            changes.Changed();
    }

    /// <summary>The folder didn't move: the paths written back, or the copies removed.</summary>
    internal async Task AbortAsync(ConnectionRuleMoveRows rows, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        if (rows.PreviousPaths is { } previous)
        {
            var ids = previous.Keys.ToList();
            foreach (
                var rule in await db.ConnectionFolderRules.Where(r => ids.Contains(r.Id)).ToListAsync(cancellationToken)
            )
                rule.Path = previous[rule.Id];
            await db.SaveChangesAsync(cancellationToken);
        }
        else
        {
            var copies = rows.AtTarget.ToList();
            await db.ConnectionFolderRules.Where(r => copies.Contains(r.Id)).ExecuteDeleteAsync(cancellationToken);
        }
        if (rows.AtTarget.Count > 0)
            changes.Changed();
    }

    /// <summary>
    /// Removes every library's rules at <paramref name="folder"/> and below it (the folder left the connection);
    /// the number removed.
    /// </summary>
    public async Task<int> RemoveTreeAsync(
        int connectionId,
        string folder,
        StringComparison comparison,
        CancellationToken cancellationToken
    )
    {
        var lockOfConnection = gate(connectionId);
        await lockOfConnection.WaitAsync(cancellationToken);
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            var rules = await db
                .ConnectionFolderRules.AsNoTracking()
                .Where(r => r.ConnectionId == connectionId)
                .Select(r => new { r.Id, r.Path })
                .ToListAsync(cancellationToken);
            var ids = rules.Where(r => RemotePaths.IsInTree(r.Path, folder, comparison)).Select(r => r.Id).ToList();
            if (ids.Count == 0)
                return 0;
            await db.ConnectionFolderRules.Where(r => ids.Contains(r.Id)).ExecuteDeleteAsync(cancellationToken);
            changes.Changed();
            return ids.Count;
        }
        finally
        {
            lockOfConnection.Release();
        }
    }

    /// <summary>Why the rules can't follow the folder; null when they can.</summary>
    private static AccessResult<ConnectionRuleMove>? Refusal(
        ConnectionMovePlan plan,
        List<ConnectionFolderRule> moving,
        List<ConnectionFolderRule> left
    )
    {
        if (moving.Any(r => ConnectionMoveComparison.MovedPath(r.Path, plan).Length > RemotePaths.MaxPathLength))
            return AccessResult<ConnectionRuleMove>.Invalid("path", "The folder's rules would get too long a path.");
        // The asking library's own are its to remove or re-point; another library's are removed once it moved.
        if (left.Any(r => r.TenantId == plan.TenantId))
        {
            return AccessResult<ConnectionRuleMove>.Conflict(
                "Folder rules are already set where the folder goes: remove them or point them elsewhere first."
            );
        }
        return null;
    }

    /// <summary>Rules on the moved folder that keep the level each of the asking library's principals had there.</summary>
    private static async Task<List<ConnectionFolderRule>> PinsAsync(
        AccessDbContext db,
        ConnectionMovePlan plan,
        List<ConnectionFolderRule> rules,
        int? changedById,
        CancellationToken cancellationToken
    )
    {
        var (groups, cap) = await DefaultsAsync(db, plan, cancellationToken);
        var own = rules.Where(r => r.TenantId == plan.TenantId).ToList();
        var now = DateTime.UtcNow;
        // An account had no say before: an account rule that adds nothing (Hidden) keeps it so.
        return
        [
            .. ConnectionMoveComparison
                .Compare(own, groups, cap, plan)
                .Select(c => new ConnectionFolderRule
                {
                    ConnectionId = plan.ConnectionId,
                    TenantId = plan.TenantId,
                    Path = plan.Target,
                    GroupId = c.Principal.GroupId,
                    UserId = c.Principal.UserId,
                    Level = c.Before ?? FolderLevel.Hidden,
                    Created = now,
                    CreatedById = changedById,
                }),
        ];
    }

    /// <summary>The same folder for the provider: the paths rewritten in place (they protect the same items).</summary>
    private static async Task<ConnectionRuleMoveRows> RenameInPlaceAsync(
        AccessDbContext db,
        ConnectionMovePlan plan,
        List<ConnectionFolderRule> moving,
        CancellationToken cancellationToken
    )
    {
        var previous = moving.ToDictionary(r => r.Id, r => r.Path);
        foreach (var rule in moving)
            rule.Path = ConnectionMoveComparison.MovedPath(rule.Path, plan);
        await db.SaveChangesAsync(cancellationToken);
        return new ConnectionRuleMoveRows(plan, [], [.. previous.Keys], [], previous, OthersOf(plan, moving));
    }

    /// <summary>The moving rules copied to their new paths, with the pins: both places protected meanwhile.</summary>
    private static async Task<ConnectionRuleMoveRows> CopyAsync(
        AccessDbContext db,
        ConnectionMovePlan plan,
        (List<ConnectionFolderRule> Moving, List<ConnectionFolderRule> Pins, List<ConnectionFolderRule> Left) rules,
        CancellationToken cancellationToken
    )
    {
        var copies = rules
            .Moving.Select(r => new ConnectionFolderRule
            {
                ConnectionId = r.ConnectionId,
                TenantId = r.TenantId,
                Path = ConnectionMoveComparison.MovedPath(r.Path, plan),
                GroupId = r.GroupId,
                UserId = r.UserId,
                Level = r.Level,
                ItemId = r.ItemId,
                Created = r.Created,
                CreatedById = r.CreatedById,
            })
            .ToList();
        db.ConnectionFolderRules.AddRange(copies);
        db.ConnectionFolderRules.AddRange(rules.Pins);
        await db.SaveChangesAsync(cancellationToken);
        return new ConnectionRuleMoveRows(
            plan,
            [.. rules.Moving.Select(r => r.Id)],
            [.. copies.Concat(rules.Pins).Select(r => r.Id)],
            [.. rules.Left.Select(r => r.Id)],
            null,
            OthersOf(plan, rules.Moving.Concat(rules.Left))
        );
    }

    /// <summary>The level each group of the asking library uses the connection at, and the read-only cap.</summary>
    private static async Task<(Dictionary<int, FolderLevel> Groups, FolderLevel Cap)> DefaultsAsync(
        AccessDbContext db,
        ConnectionMovePlan plan,
        CancellationToken cancellationToken
    )
    {
        var rows = await db
            .ConnectionGroups.AsNoTracking()
            .Where(g => g.ConnectionId == plan.ConnectionId && g.Group!.TenantId == plan.TenantId)
            .Select(g => new { g.GroupId, g.Level })
            .ToListAsync(cancellationToken);
        var readOnly = await db
            .Connections.Where(c => c.Id == plan.ConnectionId)
            .Select(c => c.ReadOnly)
            .FirstOrDefaultAsync(cancellationToken);
        var groups = rows.GroupBy(r => r.GroupId).ToDictionary(g => g.Key, g => g.Max(r => r.Level));
        return (groups, readOnly ? FolderLevel.Read : FolderLevel.Write);
    }

    private static List<int> OthersOf(ConnectionMovePlan plan, IEnumerable<ConnectionFolderRule> rules) =>
        [.. rules.Select(r => r.TenantId).Where(t => t != plan.TenantId).Distinct()];

    /// <summary>The provider's id of a moved folder, when it has one that fits.</summary>
    private static string? IdOf(RemoteItem? moved) =>
        moved is { IsFolder: true, Id: { Length: > 0 and <= ConnectionFolderRule.MaxItemIdLength } id } ? id : null;
}
