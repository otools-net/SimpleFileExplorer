using Microsoft.EntityFrameworkCore;
using Otools.Net.Sfe.Core.Data;
using Otools.Net.Sfe.Core.Paths;

namespace Otools.Net.Sfe.Core.Access;

/// <summary>
/// The folder rules of a library following a folder that is renamed or moved, for <see cref="FolderRuleService"/>:
/// what a move changes in who may do what (<see cref="CompareAsync"/>), and the move itself
/// (<see cref="BeginAsync"/>, then <see cref="FolderRuleMove"/>).
/// </summary>
/// <param name="gate">The library's lock, shared with the other rule changes.</param>
internal sealed class FolderRuleMoves(
    IDbContextFactory<AccessDbContext> dbFactory,
    AccessChanges changes,
    Func<int, SemaphoreSlim> gate
)
{
    /// <summary>See <see cref="FolderRuleService.CompareMoveAsync"/>; the paths already checked.</summary>
    public async Task<AccessResult<IReadOnlyList<FolderLevelChange>>> CompareAsync(
        int tenantId,
        (string Source, string Target) move,
        CancellationToken cancellationToken
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var rules = await db
            .FolderRules.AsNoTracking()
            .Where(r => r.TenantId == tenantId)
            .ToListAsync(cancellationToken);
        var groups = await GroupIdsAsync(db, tenantId, cancellationToken);
        return AccessResult<IReadOnlyList<FolderLevelChange>>.Ok(Compare(rules, groups, move.Source, move.Target));
    }

    /// <summary>See <see cref="FolderRuleService.BeginMoveAsync"/>; the paths already checked.</summary>
    public async Task<AccessResult<FolderRuleMove>> BeginAsync(
        int tenantId,
        (string Source, string Target) move,
        bool keepAccess,
        int? changedById,
        CancellationToken cancellationToken
    )
    {
        var lockOfLibrary = gate(tenantId);
        await lockOfLibrary.WaitAsync(cancellationToken);
        var handed = false;
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            var rules = await db.FolderRules.Where(r => r.TenantId == tenantId).ToListAsync(cancellationToken);
            var moving = rules.Where(r => FolderPolicy.IsSameOrBelow(r.Path, move.Source)).ToList();
            var caseOnly = LibraryPaths.PathComparer.Equals(move.Source, move.Target);
            var renamed = moving.Select(r => (Rule: r, Path: move.Target + r.Path[move.Source.Length..])).ToList();
            if (Refusal(rules, moving, renamed, move.Target, caseOnly) is { } refused)
                return refused;

            var pins =
                keepAccess && !caseOnly
                    ? Pins(tenantId, rules, await GroupIdsAsync(db, tenantId, cancellationToken), move, changedById)
                    : [];
            var begun = caseOnly
                ? new FolderRuleMove(
                    this,
                    lockOfLibrary,
                    tenantId,
                    [],
                    [],
                    await RenameInPlaceAsync(db, renamed, cancellationToken)
                )
                : new FolderRuleMove(
                    this,
                    lockOfLibrary,
                    tenantId,
                    [.. renamed.Select(m => m.Rule.Id)],
                    await CopyAsync(db, tenantId, renamed, pins, cancellationToken),
                    null
                );
            changes.Changed();
            handed = true;
            return AccessResult<FolderRuleMove>.Ok(begun);
        }
        finally
        {
            if (!handed)
                lockOfLibrary.Release();
        }
    }

    /// <summary>Why the move can't be done; null when it can.</summary>
    internal static (string Key, string Message)? MoveError(
        string? from,
        string? to,
        out string source,
        out string target
    )
    {
        target = string.Empty;
        if (FolderRuleService.PathError(from, out source) is { } fromError)
            return ("from", fromError);
        if (FolderRuleService.PathError(to, out target) is { } toError)
            return ("to", toError);
        if (source.Length == 0 || target.Length == 0)
            return ("from", "The library's root folder can't be moved.");
        if (string.Equals(source, target, StringComparison.Ordinal))
            return ("to", "The folder is already there.");
        if (!LibraryPaths.PathComparer.Equals(source, target) && FolderPolicy.IsBelow(target, source))
            return ("to", "A folder can't move into itself.");
        return null;
    }

    internal async Task DeleteAsync(int tenantId, IReadOnlyCollection<int> ids, CancellationToken cancellationToken)
    {
        if (ids.Count == 0)
            return;
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await db
            .FolderRules.Where(r => r.TenantId == tenantId && ids.Contains(r.Id))
            .ExecuteDeleteAsync(cancellationToken);
        changes.Changed();
    }

    internal async Task RestorePathsAsync(
        int tenantId,
        IReadOnlyDictionary<int, string> paths,
        CancellationToken cancellationToken
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var ids = paths.Keys.ToList();
        foreach (
            var rule in await db
                .FolderRules.Where(r => r.TenantId == tenantId && ids.Contains(r.Id))
                .ToListAsync(cancellationToken)
        )
        {
            rule.Path = paths[rule.Id];
        }

        await db.SaveChangesAsync(cancellationToken);
        changes.Changed();
    }

    /// <summary>Why the rules can't follow the folder; null when they can.</summary>
    private static AccessResult<FolderRuleMove>? Refusal(
        List<FolderRule> rules,
        List<FolderRule> moving,
        List<(FolderRule Rule, string Path)> renamed,
        string target,
        bool caseOnly
    )
    {
        if (renamed.FirstOrDefault(m => m.Path.Length > SfeDbContext.MaxPathLength) is { Rule: not null } tooLong)
        {
            return AccessResult<FolderRuleMove>.Invalid(
                "to",
                $"“{tooLong.Path}” is longer than {SfeDbContext.MaxPathLength} characters."
            );
        }
        // Rules where the folder goes are left from a folder that is gone: they would apply to the moved one.
        if (!caseOnly && rules.Any(r => !moving.Contains(r) && FolderPolicy.IsSameOrBelow(r.Path, target)))
        {
            return AccessResult<FolderRuleMove>.Conflict(
                "Folder rules are already set where the folder goes: remove them or point them elsewhere first."
            );
        }
        return null;
    }

    /// <summary>Rules on the moved folder that keep the level each principal had there.</summary>
    private static List<FolderRule> Pins(
        int tenantId,
        List<FolderRule> rules,
        IReadOnlyList<int> groups,
        (string Source, string Target) move,
        int? changedById
    )
    {
        var now = DateTime.UtcNow;
        // An account had no say before: an account rule that adds nothing (Hidden) keeps it so.
        return
        [
            .. Compare(rules, groups, move.Source, move.Target)
                .Select(c => new FolderRule
                {
                    TenantId = tenantId,
                    Path = move.Target,
                    GroupId = c.Principal.GroupId,
                    UserId = c.Principal.UserId,
                    Level = c.Before ?? FolderLevel.Hidden,
                    Created = now,
                    CreatedById = changedById,
                }),
        ];
    }

    /// <summary>
    /// The same folder for the file system: rewrites the paths in place (they protect the same files). The result is
    /// the paths they had, by rule.
    /// </summary>
    private static async Task<Dictionary<int, string>> RenameInPlaceAsync(
        AccessDbContext db,
        List<(FolderRule Rule, string Path)> renamed,
        CancellationToken cancellationToken
    )
    {
        var previous = renamed.ToDictionary(m => m.Rule.Id, m => m.Rule.Path);
        foreach (var (rule, path) in renamed)
            rule.Path = path;
        await db.SaveChangesAsync(cancellationToken);
        return previous;
    }

    /// <summary>
    /// Copies the moving rules to their new paths, with the pins: both places protected meanwhile. The result is the
    /// rules added.
    /// </summary>
    private static async Task<List<int>> CopyAsync(
        AccessDbContext db,
        int tenantId,
        List<(FolderRule Rule, string Path)> renamed,
        List<FolderRule> pins,
        CancellationToken cancellationToken
    )
    {
        var copies = renamed
            .Select(m => new FolderRule
            {
                TenantId = tenantId,
                Path = m.Path,
                GroupId = m.Rule.GroupId,
                UserId = m.Rule.UserId,
                Level = m.Rule.Level,
                Created = m.Rule.Created,
                CreatedById = m.Rule.CreatedById,
            })
            .ToList();
        db.FolderRules.AddRange(copies);
        db.FolderRules.AddRange(pins);
        await db.SaveChangesAsync(cancellationToken);
        return [.. copies.Concat(pins).Select(r => r.Id)];
    }

    private static Task<List<int>> GroupIdsAsync(
        AccessDbContext db,
        int tenantId,
        CancellationToken cancellationToken
    ) => db.Groups.AsNoTracking().Where(g => g.TenantId == tenantId).Select(g => g.Id).ToListAsync(cancellationToken);

    /// <summary>
    /// How the level of every group, and of every account with rules, changes at <paramref name="source"/> once it is
    /// moved to <paramref name="target"/> (only those that change).
    /// </summary>
    private static List<FolderLevelChange> Compare(
        List<FolderRule> rules,
        IReadOnlyList<int> groupIds,
        string source,
        string target
    )
    {
        var principals = groupIds
            .Select(FolderPrincipalRef.Group)
            .Concat(
                rules
                    .Where(r => r.UserId is not null)
                    .Select(r => FolderPrincipalRef.Account(r.UserId!.Value))
                    .Distinct()
            );
        var result = new List<FolderLevelChange>();
        foreach (var principal in principals)
        {
            var own = rules.Where(principal.Owns).ToList();
            var before = RightsEvaluator.RuleMap(own.Select(r => (r.Path, r.Level)));
            // After the move: the moved folder's rules at their new paths, the others where they are.
            var after = RightsEvaluator.RuleMap(
                own.Select(r =>
                    FolderPolicy.IsSameOrBelow(r.Path, source)
                        ? (target + r.Path[source.Length..], r.Level)
                        : (r.Path, r.Level)
                )
            );
            var unrestricted = principal.GroupId is not null;
            var change = new FolderLevelChange(
                principal,
                FolderPolicy.LevelAt(before, source, unrestricted),
                FolderPolicy.LevelAt(after, target, unrestricted)
            );
            if (change.Before != change.After)
                result.Add(change);
        }
        return result;
    }
}
