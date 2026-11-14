using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sfe.Core.Access;
using Sfe.Core.Data;
using Sfe.Core.Logs;

namespace Sfe.Core.Connections;

/// <summary>
/// The folder rules of library- and server-level connections: each library sets its own, for its groups and
/// accounts, replacing them all at once. Checks what keeps them meaningful (paths of the connection, principals of
/// the library, at most <see cref="MaxRules"/>) but not who asks: the Api decides that.
/// </summary>
public sealed class ConnectionRuleService(
    IDbContextFactory<AccessDbContext> dbFactory,
    ConnectionProviders providers,
    AccessChanges changes,
    ILogger<ConnectionRuleService> logger
)
{
    /// <summary>Rules a library may set in one connection: as many as in the library itself.</summary>
    public const int MaxRules = FolderRuleService.MaxRules;

    /// <summary>One lock per connection: two libraries' changes, or one library's two, wait for each other.</summary>
    private readonly ConcurrentDictionary<int, SemaphoreSlim> _locks = new();

    private ConnectionRuleMoves? _moves;

    private ConnectionRuleMoves Moves => _moves ??= new ConnectionRuleMoves(dbFactory, changes, Gate);

    /// <summary>The rules library <paramref name="tenantId"/> set in connection
    /// <paramref name="connectionId"/>.</summary>
    public async Task<IReadOnlyList<ConnectionRuleInfo>> GetAsync(
        int connectionId,
        int tenantId,
        CancellationToken cancellationToken = default
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var rules = await db
            .ConnectionFolderRules.AsNoTracking()
            .Where(r => r.ConnectionId == connectionId && r.TenantId == tenantId)
            .OrderBy(r => r.Path)
            .ThenBy(r => r.Id)
            .ToListAsync(cancellationToken);
        return [.. rules.Select(ToInfo)];
    }

    /// <summary>
    /// Replaces the rules of library <paramref name="tenantId"/> in a connection it reaches (its own, or the server's
    /// offered to it) with <paramref name="rules"/>; other libraries' rules are left as they are. Not found for a
    /// connection the library doesn't reach; invalid for an account's connection, which has no rules.
    /// </summary>
    public async Task<AccessResult<IReadOnlyList<ConnectionRuleInfo>>> SetAsync(
        int connectionId,
        int tenantId,
        IReadOnlyList<ConnectionRuleInput> rules,
        int? changedById,
        CancellationToken cancellationToken = default
    )
    {
        if (ConnectionRuleChecks.InputError(rules, out var wanted) is { } error)
            return AccessResult<IReadOnlyList<ConnectionRuleInfo>>.Invalid(error.Key, error.Message);

        var gate = Gate(connectionId);
        await gate.WaitAsync(cancellationToken);
        try
        {
            return await ReplaceAsync(new(connectionId, tenantId, changedById), wanted, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// Whether any library set a rule in the connection: without one, a move changes nobody's access.
    /// </summary>
    public async Task<bool> HasRulesAsync(int connectionId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.ConnectionFolderRules.AnyAsync(r => r.ConnectionId == connectionId, cancellationToken);
    }

    /// <summary>
    /// How the level of each group of the asking library using the connection, and of each of its accounts with
    /// rules, changes at a folder once moved (only those that change): anyone who <see cref="FolderLevelChange.Gains"/>
    /// is asked about before the move, as in the library.
    /// </summary>
    public Task<IReadOnlyList<FolderLevelChange>> CompareMoveAsync(
        ConnectionMovePlan plan,
        CancellationToken cancellationToken = default
    ) => Moves.CompareAsync(plan, cancellationToken);

    /// <summary>
    /// Begins a folder move inside a connection: the rules of every library at the folder and below it are copied to
    /// the new paths (a case-only rename rewrites them in place), with pins keeping the asking library's current
    /// access when <paramref name="keepAccess"/>. The result holds the connection's lock: move the folder, then
    /// complete or abort it (<c>await using</c>). Conflict when the asking library's rules are set where the folder
    /// goes; another library's left there are removed once it moved.
    /// </summary>
    public Task<AccessResult<ConnectionRuleMove>> BeginMoveAsync(
        ConnectionMovePlan plan,
        bool keepAccess,
        int? changedById,
        CancellationToken cancellationToken = default
    ) => Moves.BeginAsync(plan, keepAccess, changedById, cancellationToken);

    /// <summary>
    /// A folder left the connection (moved out by a transfer, its source deleted): every library's rules at it and
    /// below it go. A folder deleted through SFE keeps its rules (they show as not found and can be re-pointed).
    /// </summary>
    public async Task<int> RemoveFolderAsync(
        int connectionId,
        string folder,
        StringComparison comparison,
        CancellationToken cancellationToken = default
    )
    {
        var removed = await Moves.RemoveTreeAsync(connectionId, folder, comparison, cancellationToken);
        if (removed > 0)
        {
            logger.LogInformation(
                "Connection {ConnectionId}: {Count} folder rules removed with “{Folder}”, moved out of it",
                connectionId,
                removed,
                folder
            );
        }
        return removed;
    }

    private SemaphoreSlim Gate(int connectionId) => _locks.GetOrAdd(connectionId, _ => new SemaphoreSlim(1, 1));

    private async Task<AccessResult<IReadOnlyList<ConnectionRuleInfo>>> ReplaceAsync(
        RuleChange change,
        IReadOnlyList<ConnectionRuleInput> wanted,
        CancellationToken cancellationToken
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var connection = await db
            .Connections.Include(c => c.Tenants)
            .FirstOrDefaultAsync(c => c.Id == change.ConnectionId, cancellationToken);
        if (connection is null)
            return AccessResult<IReadOnlyList<ConnectionRuleInfo>>.Missing();
        if (connection.Level == ConnectionLevel.Account)
        {
            return AccessResult<IReadOnlyList<ConnectionRuleInfo>>.Invalid(
                "level",
                "An account's connection has no folder rules."
            );
        }
        if (!ConnectionSharing.IsReachable(connection, change.TenantId))
            return AccessResult<IReadOnlyList<ConnectionRuleInfo>>.Missing();

        var paths = providers.Find(connection.Kind) is { } provider
            ? RemotePaths.ComparerOf(provider.Capabilities)
            : StringComparer.Ordinal;
        if (ConnectionRuleChecks.HasTwins(wanted, paths))
        {
            return AccessResult<IReadOnlyList<ConnectionRuleInfo>>.Invalid(
                "path",
                "One rule per folder and principal."
            );
        }
        var principalError = await ConnectionRuleChecks.PrincipalErrorAsync(
            db,
            change.TenantId,
            wanted,
            cancellationToken
        );
        if (principalError is { } error)
            return AccessResult<IReadOnlyList<ConnectionRuleInfo>>.Invalid(error.Key, error.Message);

        var rows = await db
            .ConnectionFolderRules.Where(r => r.ConnectionId == change.ConnectionId && r.TenantId == change.TenantId)
            .ToListAsync(cancellationToken);
        var openedToMore = Apply(db, rows, wanted, paths, change);
        await db.SaveChangesAsync(cancellationToken);
        changes.Changed();

        var kept = rows.Where(r => db.Entry(r).State != EntityState.Detached)
            .OrderBy(r => r.Path, StringComparer.Ordinal)
            .ThenBy(r => r.Id);
        IReadOnlyList<ConnectionRuleInfo> result = [.. kept.Select(ToInfo)];
        Log(connection, change, result, openedToMore);
        return AccessResult<IReadOnlyList<ConnectionRuleInfo>>.Ok(result);
    }

    /// <summary>
    /// Changes <paramref name="rows"/> into <paramref name="wanted"/>: rows that stay are changed in place (EF refuses
    /// a row removed and added again under the same key), the others removed, new ones added to
    /// <paramref name="rows"/>. True when a rule opens more than before (<see cref="Raises"/>).
    /// </summary>
    private static bool Apply(
        AccessDbContext db,
        List<ConnectionFolderRule> rows,
        IReadOnlyList<ConnectionRuleInput> wanted,
        StringComparer paths,
        RuleChange change
    )
    {
        var unmatched = rows.ToList();
        var openedToMore = false;
        foreach (var rule in wanted)
        {
            var row = unmatched.FirstOrDefault(r =>
                r.GroupId == rule.GroupId && r.UserId == rule.UserId && paths.Equals(r.Path, rule.Path)
            );
            if (row is null)
            {
                row = new ConnectionFolderRule
                {
                    ConnectionId = change.ConnectionId,
                    TenantId = change.TenantId,
                    GroupId = rule.GroupId,
                    UserId = rule.UserId,
                };
                db.ConnectionFolderRules.Add(row);
                rows.Add(row);
            }
            else
            {
                unmatched.Remove(row);
            }
            openedToMore |= Raises(row.Id == 0 ? null : row.Level, rule.Level);
            Update(row, rule, change.ChangedById);
        }

        openedToMore |= unmatched.Any(r => Raises(r.Level, null));
        db.ConnectionFolderRules.RemoveRange(unmatched);
        return openedToMore;
    }

    private static void Update(ConnectionFolderRule row, ConnectionRuleInput rule, int? changedById)
    {
        if (row.Id != 0 && row.Path == rule.Path && row.Level == rule.Level && row.ItemId == rule.ItemId)
            return;
        row.Path = rule.Path!;
        row.Level = rule.Level;
        row.ItemId = rule.ItemId;
        row.Created = DateTime.UtcNow;
        row.CreatedById = changedById;
    }

    /// <summary>
    /// A rule that opens a folder to more: a new rule allowing Write, a rule raised, or a Hidden rule removed
    /// (what was hidden may now be seen). A removed Read rule follows the folder above: told as a change only.
    /// </summary>
    private static bool Raises(FolderLevel? before, FolderLevel? after) =>
        (before, after) switch
        {
            (null, var level) => level == FolderLevel.Write,
            (var level, null) => level == FolderLevel.Hidden,
            var (was, now) => now > was,
        };

    private void Log(
        Connection connection,
        RuleChange change,
        IReadOnlyList<ConnectionRuleInfo> rules,
        bool openedToMore
    )
    {
        var described = string.Join(
            ", ",
            rules.Select(r => $"{(r.GroupId is { } g ? $"group {g}" : $"account {r.UserId}")} {r.Level} “{r.Path}”")
        );
        if (openedToMore)
        {
            logger.LogSecurityWarning(
                "Connection {Connection} folder rules opened to more by account {UserId} in library {TenantId}: "
                    + "[{Rules}]",
                ConnectionRows.Describe(connection),
                change.ChangedById,
                change.TenantId,
                described
            );
        }
        else
        {
            logger.LogInformation(
                "Connection {Connection} folder rules set by account {UserId} in library {TenantId}: [{Rules}]",
                ConnectionRows.Describe(connection),
                change.ChangedById,
                change.TenantId,
                described
            );
        }
    }

    private static ConnectionRuleInfo ToInfo(ConnectionFolderRule rule) =>
        new(rule.Id, rule.Path, rule.GroupId, rule.UserId, rule.Level, rule.ItemId);

    /// <summary>Whose rules change, and who changes them.</summary>
    private sealed record RuleChange(int ConnectionId, int TenantId, int? ChangedById);
}
