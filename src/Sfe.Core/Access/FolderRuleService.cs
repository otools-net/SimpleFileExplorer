using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Sfe.Core.Data;
using Sfe.Core.Paths;

namespace Sfe.Core.Access;

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

    private FolderRuleMoves? _moves;

    public async Task<IReadOnlyList<FolderRuleInfo>> GetAsync(
        int tenantId,
        CancellationToken cancellationToken = default
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var rules = await db
            .FolderRules.AsNoTracking()
            .Where(r => r.TenantId == tenantId)
            .OrderBy(r => r.Path)
            .ThenBy(r => r.Id)
            .ToListAsync(cancellationToken);
        return [.. rules.Select(ToInfo)];
    }

    /// <summary>
    /// Sets the level of <paramref name="principal"/> at <paramref name="path"/>, replacing its rule there; a null
    /// <paramref name="level"/> removes the rule (the folder inherits again). The result is the rule, null when
    /// removed.
    /// </summary>
    public async Task<AccessResult<FolderRuleInfo?>> SetAsync(
        int tenantId,
        string? path,
        FolderPrincipalRef principal,
        FolderLevel? level,
        int? changedById,
        CancellationToken cancellationToken = default
    )
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
            {
                return AccessResult<FolderRuleInfo?>.Invalid(
                    principal.GroupId is null ? "userId" : "groupId",
                    principalError
                );
            }

            var rules = await db.FolderRules.Where(r => r.TenantId == tenantId).ToListAsync(cancellationToken);
            var existing = rules
                .Where(r => principal.Owns(r) && LibraryPaths.PathComparer.Equals(r.Path, normalized))
                .ToList();
            FolderRule? kept = null;
            if (level is { } wanted)
            {
                if (existing.Count == 0 && rules.Count >= MaxRules)
                {
                    return AccessResult<FolderRuleInfo?>.Conflict(
                        $"A space can have at most {MaxRules} folder rules: remove some first."
                    );
                }

                kept = existing.FirstOrDefault();
                if (kept is null)
                {
                    kept = new FolderRule
                    {
                        TenantId = tenantId,
                        GroupId = principal.GroupId,
                        UserId = principal.UserId,
                    };
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
        int tenantId,
        string? from,
        string? to,
        CancellationToken cancellationToken = default
    )
    {
        if (FolderRuleMoves.MoveError(from, to, out var source, out var target) is { } error)
            return AccessResult<IReadOnlyList<FolderLevelChange>>.Invalid(error.Key, error.Message);
        return await Moves.CompareAsync(tenantId, (source, target), cancellationToken);
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
        int tenantId,
        string? from,
        string? to,
        bool keepAccess,
        int? changedById,
        CancellationToken cancellationToken = default
    )
    {
        if (FolderRuleMoves.MoveError(from, to, out var source, out var target) is { } error)
            return AccessResult<FolderRuleMove>.Invalid(error.Key, error.Message);
        return await Moves.BeginAsync(tenantId, (source, target), keepAccess, changedById, cancellationToken);
    }

    /// <summary>
    /// Takes away the rules of <paramref name="folder"/> and of everything below it, for every principal: the folder
    /// left the library (deleted to the trash, or for good). The result keeps them, relative to the folder, for
    /// <see cref="PutBackAsync"/> when it comes back.
    /// </summary>
    public async Task<IReadOnlyList<KeptFolderRule>> TakeTreeAsync(
        int tenantId,
        string folder,
        CancellationToken cancellationToken = default
    )
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
            return
            [
                .. rules.Select(r => new KeptFolderRule(
                    r.Path[source.Length..].TrimStart('/'),
                    r.GroupId,
                    r.UserId,
                    r.Level,
                    r.Created,
                    r.CreatedById
                )),
            ];
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// Gives back rules taken by <see cref="TakeTreeAsync"/> to the folder now at <paramref name="folder"/> (restored,
    /// maybe under another name). Rules of groups deleted meanwhile, or of accounts no longer in the library, are
    /// dropped.
    /// </summary>
    /// <returns>Rules put back.</returns>
    public async Task<int> PutBackAsync(
        int tenantId,
        string folder,
        IReadOnlyList<KeptFolderRule> rules,
        CancellationToken cancellationToken = default
    )
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
                if (
                    await PrincipalErrorAsync(
                        db,
                        tenantId,
                        new FolderPrincipalRef(rule.GroupId, rule.UserId),
                        cancellationToken
                    )
                    is not null
                )
                {
                    continue;
                }

                kept.Add(
                    new FolderRule
                    {
                        TenantId = tenantId,
                        Path = rule.Path.Length == 0 ? target : $"{target}/{rule.Path}",
                        GroupId = rule.GroupId,
                        UserId = rule.UserId,
                        Level = rule.Level,
                        Created = rule.Created,
                        CreatedById = rule.CreatedById,
                    }
                );
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

    private FolderRuleMoves Moves => _moves ??= new FolderRuleMoves(dbFactory, changes, Gate);

    private SemaphoreSlim Gate(int tenantId) => _locks.GetOrAdd(tenantId, _ => new SemaphoreSlim(1, 1));

    /// <summary>Why a rule can't be set at the path; null when it can, with the stored form of the path.</summary>
    internal static string? PathError(string? path, out string normalized)
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

    private static async Task<string?> PrincipalErrorAsync(
        AccessDbContext db,
        int tenantId,
        FolderPrincipalRef principal,
        CancellationToken cancellationToken
    )
    {
        if ((principal.GroupId is null) == (principal.UserId is null))
            return "Name a group or an account.";
        if (principal.GroupId is { } groupId)
        {
            return await db.Groups.AnyAsync(g => g.Id == groupId && g.TenantId == tenantId, cancellationToken)
                ? null
                : "No such group in this space.";
        }

        var userId = principal.UserId!.Value;
        return await db.GroupMembers.AnyAsync(
            m => m.UserId == userId && m.Group!.TenantId == tenantId,
            cancellationToken
        )
            ? null
            : "The account isn't in this space.";
    }

    private static FolderRuleInfo ToInfo(FolderRule rule) =>
        new(
            rule.Id,
            rule.Path,
            new FolderPrincipalRef(rule.GroupId, rule.UserId),
            rule.Level,
            rule.Created,
            rule.CreatedById
        );
}
