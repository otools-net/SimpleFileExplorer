using Otools.Net.Sfe.Core.Access;
using Otools.Net.Sfe.Core.Data;

namespace Otools.Net.Sfe.Core.Connections;

/// <summary>
/// What a folder moved inside a connection changes for one library's groups and accounts, compared as the library
/// compares its own moves (<see cref="FolderLevelChange"/>), with a connection's defaults: a group uses the
/// connection at its <see cref="ConnectionGroup"/> row's level where no rule of it applies (a group without a row
/// sees nothing, wherever the folder is), an account has no say without a rule, a read-only connection caps all.
/// </summary>
internal static class ConnectionMoveComparison
{
    /// <summary>
    /// How the level of every group with a row (<paramref name="groups"/>: its level), and of every account with
    /// rules, changes at the folder once moved (only those that change). <paramref name="rules"/>: the library's.
    /// </summary>
    public static List<FolderLevelChange> Compare(
        IReadOnlyList<ConnectionFolderRule> rules,
        IReadOnlyDictionary<int, FolderLevel> groups,
        FolderLevel cap,
        ConnectionMovePlan plan
    )
    {
        var principals = groups
            .Keys.Select(FolderPrincipalRef.Group)
            .Concat(rules.Where(r => r.UserId is not null).Select(r => FolderPrincipalRef.Account(r.UserId!.Value)))
            .Distinct();
        var result = new List<FolderLevelChange>();
        foreach (var principal in principals)
        {
            var own = rules.Where(r => r.GroupId == principal.GroupId && r.UserId == principal.UserId).ToList();
            FolderLevel? fallback = principal.GroupId is { } groupId ? groups[groupId] : null;
            var before = LevelAt(own.Select(r => (r.Path, r.Level)), plan.Source, plan, fallback);
            var after = LevelAt(own.Select(r => (MovedPath(r.Path, plan), r.Level)), plan.Target, plan, fallback);
            var change = new FolderLevelChange(principal, Capped(before, cap), Capped(after, cap));
            if (change.Before != change.After)
                result.Add(change);
        }
        return result;
    }

    /// <summary>Where a rule at <paramref name="path"/> is once the folder moved (unchanged outside it).</summary>
    public static string MovedPath(string path, ConnectionMovePlan plan) =>
        RemotePaths.IsInTree(path, plan.Source, plan.Comparison) ? plan.Target + path[plan.Source.Length..] : path;

    private static FolderLevel? LevelAt(
        IEnumerable<(string Path, FolderLevel Level)> rules,
        string path,
        ConnectionMovePlan plan,
        FolderLevel? fallback
    )
    {
        var comparer = StringComparer.FromComparison(plan.Comparison);
        // Two rows for one path (concurrent changes) count as the stricter one, as the policy reads them.
        var map = rules.GroupBy(r => r.Path, comparer).ToDictionary(g => g.Key, g => g.Min(r => r.Level), comparer);
        return FolderPolicy.LevelAt(map, path, unrestrictedByDefault: false) ?? fallback;
    }

    private static FolderLevel? Capped(FolderLevel? level, FolderLevel cap) => level > cap ? cap : level;
}
