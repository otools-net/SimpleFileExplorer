using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Sfe.Core.Data;
using Sfe.Core.Paths;

namespace Sfe.Core.Access;

/// <summary>Who a folder rule is for: a group of the library, or one account.</summary>
public sealed record FolderPrincipalRef(int? GroupId, int? UserId)
{
    public static FolderPrincipalRef Group(int id) => new(id, null);
    public static FolderPrincipalRef Account(int id) => new(null, id);

    internal bool Owns(FolderRule rule) => rule.GroupId == GroupId && rule.UserId == UserId;
}

/// <summary>A folder rule kept while its folder is out of the library (in the trash); its path relative to that folder.</summary>
public sealed record KeptFolderRule(string Path, int? GroupId, int? UserId, FolderLevel Level, DateTime Created, int? CreatedById);

public sealed record FolderRuleInfo(int Id, string Path, FolderPrincipalRef Principal, FolderLevel Level, DateTime Created, int? CreatedById);

/// <summary>
/// How a principal's level at a folder changes when the folder moves. Null for an account without a rule above the
/// place: it has no say there (its groups decide).
/// </summary>
public sealed record FolderLevelChange(FolderPrincipalRef Principal, FolderLevel? Before, FolderLevel? After)
{
    /// <summary>
    /// The principal may do more than before at the folder (sees it, or may change its files). An account at Hidden
    /// adds nothing to its groups, as one without a say.
    /// </summary>
    public bool Gains => (After ?? FolderLevel.Hidden) > (Before ?? FolderLevel.Hidden);
}

/// <summary>
/// The folder rules of each library: set and removed one by one, and moved with their folders when a folder is
/// renamed or moved (agents/docs/development/file-operations.md). Checks what keeps the rules meaningful (a folder
/// path, a principal of the library, at most <see cref="MaxRules"/>) but not who asks: the Api decides that.
/// </summary>
public sealed class FolderRuleService(IDbContextFactory<AccessDbContext> dbFactory, AccessChanges changes)
{
    /// <summary>
    /// Rules a library may have: each rule is a LIKE prefix in the queries of the subjects it concerns
    /// (<see cref="PathFilter.ToPredicate{T}"/>), and SQL Server takes at most 2100 parameters.
    /// </summary>
    public const int MaxRules = 250;

    /// <summary>One lock per library: rule changes wait for a move in progress, and the other way round.</summary>
    private readonly ConcurrentDictionary<int, SemaphoreSlim> _locks = new();

    public async Task<IReadOnlyList<FolderRuleInfo>> GetAsync(int tenantId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var rules = await db.FolderRules.AsNoTracking().Where(r => r.TenantId == tenantId).OrderBy(r => r.Path).ThenBy(r => r.Id).ToListAsync(cancellationToken);
        return [.. rules.Select(ToInfo)];
    }

    /// <summary>
    /// Sets the level of <paramref name="principal"/> at <paramref name="path"/>, replacing its rule there; a null
    /// <paramref name="level"/> removes the rule (the folder inherits again). The result is the rule, null when removed.
    /// </summary>
    public async Task<AccessResult<FolderRuleInfo?>> SetAsync(
        int tenantId, string? path, FolderPrincipalRef principal, FolderLevel? level, int? changedById, CancellationToken cancellationToken = default)
    {
        if (PathError(path, out var normalized) is { } error)
            return AccessResult<FolderRuleInfo?>.Invalid("path", error);
        if (level is { } value && !Enum.IsDefined(value))
            return AccessResult<FolderRuleInfo?>.Invalid("level", "Unknown level.");

        var gate = Gate(tenantId);
        await gate.WaitAsync(cancellationToken);
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            if (await PrincipalErrorAsync(db, tenantId, principal, cancellationToken) is { } principalError)
                return AccessResult<FolderRuleInfo?>.Invalid(principal.GroupId is null ? "userId" : "groupId", principalError);

            var rules = await db.FolderRules.Where(r => r.TenantId == tenantId).ToListAsync(cancellationToken);
            var existing = rules.Where(r => principal.Owns(r) && LibraryPaths.PathComparer.Equals(r.Path, normalized)).ToList();
            FolderRule? kept = null;
            if (level is { } wanted)
            {
                if (existing.Count == 0 && rules.Count >= MaxRules)
                    return AccessResult<FolderRuleInfo?>.Conflict($"A space can have at most {MaxRules} folder rules: remove some first.");
                kept = existing.FirstOrDefault();
                if (kept is null)
                {
                    kept = new FolderRule { TenantId = tenantId, GroupId = principal.GroupId, UserId = principal.UserId };
                    db.FolderRules.Add(kept);
                }
                kept.Path = normalized;
                kept.Level = wanted;
                kept.Created = DateTime.UtcNow;
                kept.CreatedById = changedById;
            }
            db.FolderRules.RemoveRange(existing.Where(r => r != kept));
            await db.SaveChangesAsync(cancellationToken);
            changes.Changed();
            return AccessResult<FolderRuleInfo?>.Ok(kept is null ? null : ToInfo(kept));
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// How the level of every group of the library, and of every account with rules there, changes at
    /// <paramref name="from"/> once it is moved to <paramref name="to"/> (only those that change): its own rules move
    /// with it, but what it inherited comes from its new parent. Asked before a move, to warn about who gains access.
    /// </summary>
    public async Task<AccessResult<IReadOnlyList<FolderLevelChange>>> CompareMoveAsync(
        int tenantId, string? from, string? to, CancellationToken cancellationToken = default)
    {
        if (MoveError(from, to, out var source, out var target) is { } error)
            return AccessResult<IReadOnlyList<FolderLevelChange>>.Invalid(error.Key, error.Message);

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var rules = await db.FolderRules.AsNoTracking().Where(r => r.TenantId == tenantId).ToListAsync(cancellationToken);
        var groups = await db.Groups.AsNoTracking().Where(g => g.TenantId == tenantId).Select(g => g.Id).ToListAsync(cancellationToken);
        return AccessResult<IReadOnlyList<FolderLevelChange>>.Ok(Compare(rules, groups, source, target));
    }

    /// <summary>
    /// First step of renaming or moving the folder <paramref name="from"/> to <paramref name="to"/> (root-relative):
    /// copies the rules of the folder and of everything below it, for every principal, to the new paths, so that both
    /// places are protected while the disk changes. Then <see cref="FolderRuleMove.CompleteAsync"/> (the disk changed)
    /// or <see cref="FolderRuleMove.AbortAsync"/> (it didn't). With <paramref name="keepAccess"/>, rules on the moved
    /// folder also pin the level each principal had there, so that the new parent changes nothing.
    /// </summary>
    /// <remarks>
    /// The move holds the library's lock until it is completed, aborted or disposed: dispose it. Disposing it without
    /// completing or aborting leaves both sets of rules (never less protection, only stale rules).
    /// </remarks>
    public async Task<AccessResult<FolderRuleMove>> BeginMoveAsync(
        int tenantId, string? from, string? to, bool keepAccess, int? changedById, CancellationToken cancellationToken = default)
    {
        if (MoveError(from, to, out var source, out var target) is { } error)
            return AccessResult<FolderRuleMove>.Invalid(error.Key, error.Message);

        var gate = Gate(tenantId);
        await gate.WaitAsync(cancellationToken);
        var handed = false;
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            var rules = await db.FolderRules.Where(r => r.TenantId == tenantId).ToListAsync(cancellationToken);
            var moving = rules.Where(r => FolderPolicy.IsSameOrBelow(r.Path, source)).ToList();
            var caseOnly = LibraryPaths.PathComparer.Equals(source, target);
            var renamed = moving.Select(r => (Rule: r, Path: target + r.Path[source.Length..])).ToList();
            if (renamed.FirstOrDefault(m => m.Path.Length > SfeDbContext.MaxPathLength) is { Rule: not null } tooLong)
                return AccessResult<FolderRuleMove>.Invalid("to", $"“{tooLong.Path}” is longer than {SfeDbContext.MaxPathLength} characters.");
            // Rules where the folder goes are left from a folder that is gone: they would apply to the moved one.
            if (!caseOnly && rules.Any(r => !moving.Contains(r) && FolderPolicy.IsSameOrBelow(r.Path, target)))
                return AccessResult<FolderRuleMove>.Conflict("Folder rules are already set where the folder goes: remove them or point them elsewhere first.");

            var now = DateTime.UtcNow;
            var pins = new List<FolderRule>();
            if (keepAccess && !caseOnly)
            {
                var groups = await db.Groups.AsNoTracking().Where(g => g.TenantId == tenantId).Select(g => g.Id).ToListAsync(cancellationToken);
                // An account had no say before: an account rule that adds nothing (Hidden) keeps it so.
                pins.AddRange(Compare(rules, groups, source, target).Select(c => new FolderRule
                {
                    TenantId = tenantId, Path = target, GroupId = c.Principal.GroupId, UserId = c.Principal.UserId,
                    Level = c.Before ?? FolderLevel.Hidden, Created = now, CreatedById = changedById,
                }));
            }

            FolderRuleMove move;
            if (caseOnly)
            {
                // The same folder for the file system: rewrite the paths in place (they protect the same files).
                var previous = moving.ToDictionary(r => r.Id, r => r.Path);
                foreach (var (rule, path) in renamed)
                    rule.Path = path;
                await db.SaveChangesAsync(cancellationToken);
                move = new FolderRuleMove(this, gate, tenantId, [], [], previous);
            }
            else
            {
                var copies = renamed.Select(m => new FolderRule
                {
                    TenantId = tenantId, Path = m.Path, GroupId = m.Rule.GroupId, UserId = m.Rule.UserId, Level = m.Rule.Level,
                    Created = m.Rule.Created, CreatedById = m.Rule.CreatedById,
                }).ToList();
                db.FolderRules.AddRange(copies);
                db.FolderRules.AddRange(pins);
                await db.SaveChangesAsync(cancellationToken);
                move = new FolderRuleMove(this, gate, tenantId, [.. moving.Select(r => r.Id)], [.. copies.Concat(pins).Select(r => r.Id)], null);
            }
            changes.Changed();
            handed = true;
            return AccessResult<FolderRuleMove>.Ok(move);
        }
        finally
        {
            if (!handed)
                gate.Release();
        }
    }

    /// <summary>
    /// Takes away the rules of <paramref name="folder"/> and of everything below it, for every principal: the folder
    /// left the library (deleted to the trash, or for good). The result keeps them, relative to the folder, for
    /// <see cref="PutBackAsync"/> when it comes back.
    /// </summary>
    public async Task<IReadOnlyList<KeptFolderRule>> TakeTreeAsync(int tenantId, string folder, CancellationToken cancellationToken = default)
    {
        if (LibraryPaths.NormalizeRelative(folder) is not { Length: > 0 } source)
            return [];
        var gate = Gate(tenantId);
        await gate.WaitAsync(cancellationToken);
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            var rules = (await db.FolderRules.Where(r => r.TenantId == tenantId).ToListAsync(cancellationToken))
                .Where(r => FolderPolicy.IsSameOrBelow(r.Path, source))
                .ToList();
            if (rules.Count == 0)
                return [];
            db.FolderRules.RemoveRange(rules);
            await db.SaveChangesAsync(cancellationToken);
            changes.Changed();
            return [.. rules.Select(r => new KeptFolderRule(r.Path[source.Length..].TrimStart('/'), r.GroupId, r.UserId, r.Level, r.Created, r.CreatedById))];
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// Gives back rules taken by <see cref="TakeTreeAsync"/> to the folder now at <paramref name="folder"/> (restored,
    /// maybe under another name). Rules of groups deleted meanwhile, or of accounts no longer in the library, are dropped.
    /// </summary>
    /// <returns>Rules put back.</returns>
    public async Task<int> PutBackAsync(int tenantId, string folder, IReadOnlyList<KeptFolderRule> rules, CancellationToken cancellationToken = default)
    {
        if (rules.Count == 0 || LibraryPaths.NormalizeRelative(folder) is not { Length: > 0 } target)
            return 0;
        var gate = Gate(tenantId);
        await gate.WaitAsync(cancellationToken);
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            var kept = new List<FolderRule>();
            foreach (var rule in rules)
            {
                if (await PrincipalErrorAsync(db, tenantId, new FolderPrincipalRef(rule.GroupId, rule.UserId), cancellationToken) is not null)
                    continue;
                kept.Add(new FolderRule
                {
                    TenantId = tenantId, Path = rule.Path.Length == 0 ? target : $"{target}/{rule.Path}", GroupId = rule.GroupId, UserId = rule.UserId,
                    Level = rule.Level, Created = rule.Created, CreatedById = rule.CreatedById,
                });
            }
            db.FolderRules.AddRange(kept);
            await db.SaveChangesAsync(cancellationToken);
            changes.Changed();
            return kept.Count;
        }
        finally
        {
            gate.Release();
        }
    }

    internal async Task DeleteAsync(int tenantId, IReadOnlyCollection<int> ids, CancellationToken cancellationToken)
    {
        if (ids.Count == 0)
            return;
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await db.FolderRules.Where(r => r.TenantId == tenantId && ids.Contains(r.Id)).ExecuteDeleteAsync(cancellationToken);
        changes.Changed();
    }

    internal async Task RestorePathsAsync(int tenantId, IReadOnlyDictionary<int, string> paths, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var ids = paths.Keys.ToList();
        foreach (var rule in await db.FolderRules.Where(r => r.TenantId == tenantId && ids.Contains(r.Id)).ToListAsync(cancellationToken))
            rule.Path = paths[rule.Id];
        await db.SaveChangesAsync(cancellationToken);
        changes.Changed();
    }

    private SemaphoreSlim Gate(int tenantId) => _locks.GetOrAdd(tenantId, _ => new SemaphoreSlim(1, 1));

    private static List<FolderLevelChange> Compare(List<FolderRule> rules, IReadOnlyList<int> groupIds, string source, string target)
    {
        var principals = groupIds.Select(FolderPrincipalRef.Group)
            .Concat(rules.Where(r => r.UserId is not null).Select(r => FolderPrincipalRef.Account(r.UserId!.Value)).Distinct());
        var result = new List<FolderLevelChange>();
        foreach (var principal in principals)
        {
            var own = rules.Where(principal.Owns).ToList();
            var before = RightsEvaluator.RuleMap(own.Select(r => (r.Path, r.Level)));
            // After the move: the moved folder's rules at their new paths, the others where they are.
            var after = RightsEvaluator.RuleMap(own.Select(r => FolderPolicy.IsSameOrBelow(r.Path, source)
                ? (target + r.Path[source.Length..], r.Level)
                : (r.Path, r.Level)));
            var unrestricted = principal.GroupId is not null;
            var change = new FolderLevelChange(principal, FolderPolicy.LevelAt(before, source, unrestricted), FolderPolicy.LevelAt(after, target, unrestricted));
            if (change.Before != change.After)
                result.Add(change);
        }
        return result;
    }

    /// <summary>Why a rule can't be set at the path; null when it can, with the stored form of the path.</summary>
    private static string? PathError(string? path, out string normalized)
    {
        normalized = LibraryPaths.NormalizeRelative(path) ?? string.Empty;
        if (LibraryPaths.NormalizeRelative(path) is null)
            return "Not a folder of the library.";
        if (normalized.Length > SfeDbContext.MaxPathLength)
            return $"The path can have at most {SfeDbContext.MaxPathLength} characters.";
        // Windows drops them when it opens a path: "Private." is "Private", so a rule on it would protect nothing.
        if (OperatingSystem.IsWindows() && normalized.Split('/').Any(s => s.EndsWith('.') || s.EndsWith(' ')))
            return "Folder names can't end with a dot or a space.";
        return null;
    }

    private static (string Key, string Message)? MoveError(string? from, string? to, out string source, out string target)
    {
        target = string.Empty;
        if (PathError(from, out source) is { } fromError)
            return ("from", fromError);
        if (PathError(to, out target) is { } toError)
            return ("to", toError);
        if (source.Length == 0 || target.Length == 0)
            return ("from", "The library's root folder can't be moved.");
        if (string.Equals(source, target, StringComparison.Ordinal))
            return ("to", "The folder is already there.");
        if (!LibraryPaths.PathComparer.Equals(source, target) && FolderPolicy.IsBelow(target, source))
            return ("to", "A folder can't move into itself.");
        return null;
    }

    private static async Task<string?> PrincipalErrorAsync(AccessDbContext db, int tenantId, FolderPrincipalRef principal, CancellationToken cancellationToken)
    {
        if ((principal.GroupId is null) == (principal.UserId is null))
            return "Name a group or an account.";
        if (principal.GroupId is { } groupId)
            return await db.Groups.AnyAsync(g => g.Id == groupId && g.TenantId == tenantId, cancellationToken) ? null : "No such group in this space.";
        var userId = principal.UserId!.Value;
        return await db.GroupMembers.AnyAsync(m => m.UserId == userId && m.Group!.TenantId == tenantId, cancellationToken)
            ? null
            : "The account isn't in this space.";
    }

    private static FolderRuleInfo ToInfo(FolderRule rule) =>
        new(rule.Id, rule.Path, new FolderPrincipalRef(rule.GroupId, rule.UserId), rule.Level, rule.Created, rule.CreatedById);
}

/// <summary>
/// A folder move in progress for the rules (<see cref="FolderRuleService.BeginMoveAsync"/>): holds the library's
/// lock until it is completed, aborted or disposed.
/// </summary>
public sealed class FolderRuleMove : IAsyncDisposable
{
    private readonly FolderRuleService _service;
    private readonly SemaphoreSlim _gate;
    private readonly int _tenantId;
    private readonly IReadOnlyList<int> _originals;
    private readonly IReadOnlyList<int> _copies;
    private readonly IReadOnlyDictionary<int, string>? _previousPaths;
    private int _released;

    internal FolderRuleMove(FolderRuleService service, SemaphoreSlim gate, int tenantId, IReadOnlyList<int> originals, IReadOnlyList<int> copies,
        IReadOnlyDictionary<int, string>? previousPaths)
    {
        _service = service;
        _gate = gate;
        _tenantId = tenantId;
        _originals = originals;
        _copies = copies;
        _previousPaths = previousPaths;
    }

    /// <summary>The folder moved on the disk: the rules at the old paths go.</summary>
    public async Task CompleteAsync(CancellationToken cancellationToken = default)
    {
        EnsureOpen();
        try
        {
            await _service.DeleteAsync(_tenantId, _originals, cancellationToken);
        }
        finally
        {
            Release();
        }
    }

    /// <summary>The folder didn't move: the rules are as they were before the move began.</summary>
    public async Task AbortAsync(CancellationToken cancellationToken = default)
    {
        EnsureOpen();
        try
        {
            if (_previousPaths is not null)
                await _service.RestorePathsAsync(_tenantId, _previousPaths, cancellationToken);
            else
                await _service.DeleteAsync(_tenantId, _copies, cancellationToken);
        }
        finally
        {
            Release();
        }
    }

    /// <summary>Releases the lock; without <see cref="CompleteAsync"/> or <see cref="AbortAsync"/> both sets of rules stay.</summary>
    public ValueTask DisposeAsync()
    {
        Release();
        return ValueTask.CompletedTask;
    }

    private void EnsureOpen() => ObjectDisposedException.ThrowIf(Volatile.Read(ref _released) != 0, this);

    private void Release()
    {
        if (Interlocked.Exchange(ref _released, 1) == 0)
            _gate.Release();
    }
}
