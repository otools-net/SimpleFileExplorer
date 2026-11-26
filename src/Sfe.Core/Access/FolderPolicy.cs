using Otools.Net.Sfe.Core.Paths;

namespace Otools.Net.Sfe.Core.Access;

/// <summary>
/// Where a subject may use its rights in a library, compiled from the folder rules of its principals
/// (agents/docs/features/folder-access.md). For each principal the nearest rule at or above a path decides its level
/// there; a right whose <see cref="RightDefinition.AtFolder"/> that level reaches is granted by that principal; the
/// subject has the union of what its principals grant. Rights of the whole space (no <c>AtFolder</c>) apply everywhere.
/// </summary>
/// <remarks>
/// The table holds the root and every path some principal has a rule on, each with the subject's rights there. It is
/// exact: no principal has a rule between a path and the deepest table entry above it, so every principal's nearest
/// rule, hence the rights, are the same for both. Without any rule (nor default level) the policy is
/// <see cref="Unrestricted"/>. Paths are compared as the place they belong to compares them: the library as its host
/// (<see cref="LibraryPaths.PathComparison"/>), a connection as its provider.
/// </remarks>
public sealed class FolderPolicy
{
    /// <summary>No folder rule applies: the subject's rights apply everywhere.</summary>
    public static FolderPolicy Unrestricted { get; } = new(null, LibraryPaths.PathComparison, false);

    /// <summary>Rights by path (compared by <see cref="_comparer"/>); null when unrestricted.</summary>
    private readonly Dictionary<string, IReadOnlySet<string>>? _entries;

    private readonly StringComparison _comparison;
    private readonly StringComparer _comparer;

    /// <summary>Some principal has a rule on the root itself (the root is in the table either way).</summary>
    private readonly bool _ruleAtRoot;

    private FolderPolicy(
        Dictionary<string, IReadOnlySet<string>>? entries,
        StringComparison comparison,
        bool ruleAtRoot
    )
    {
        _entries = entries;
        _ruleAtRoot = ruleAtRoot;
        _comparison = comparison;
        _comparer = StringComparer.FromComparison(comparison);
    }

    public bool IsUnrestricted => _entries is null;

    /// <summary>
    /// The paths with a rule of some principal, with the root: where what the subject may do can change.
    /// </summary>
    public IEnumerable<string> Paths => _entries is null ? [string.Empty] : _entries.Keys;

    /// <summary>The policy of a library folder: paths compared as the host compares them.</summary>
    public static FolderPolicy Compile(IReadOnlyList<FolderPrincipal> principals) =>
        Compile(principals, LibraryPaths.PathComparison);

    /// <summary>The policy of a tree whose paths compare by <paramref name="comparison"/>.</summary>
    public static FolderPolicy Compile(IReadOnlyList<FolderPrincipal> principals, StringComparison comparison)
    {
        if (principals.All(p => p.Rules.Count == 0 && p.DefaultLevel is null))
            return Unrestricted;

        var comparer = StringComparer.FromComparison(comparison);
        var normalized = principals
            .Select(p => p with { Rules = new Dictionary<string, FolderLevel>(p.Rules, comparer) })
            .ToList();
        var paths = new HashSet<string>(comparer) { string.Empty };
        foreach (var principal in normalized)
            paths.UnionWith(principal.Rules.Keys);
        return new FolderPolicy(
            paths.ToDictionary(path => path, IReadOnlySet<string> (path) => RightsAt(normalized, path), comparer),
            comparison,
            normalized.Any(p => p.Rules.ContainsKey(string.Empty))
        );
    }

    /// <summary>
    /// What the subject may do at <paramref name="path"/> (root-relative, already canonical), of the rights it was
    /// <paramref name="granted"/> in the library.
    /// </summary>
    public FolderAccess At(string path, IReadOnlySet<string> granted)
    {
        if (_entries is null)
            return new FolderAccess(granted, null, false);
        var rights = Nearest(path);
        return new FolderAccess(granted, rights, IsPathOnly(path, rights, granted));
    }

    /// <summary>
    /// The folder whose rule decides what the subject may do at <paramref name="path"/>: the nearest at or above it
    /// that some principal has a rule on. Null without folder rules, or when only default levels apply there.
    /// </summary>
    public string? RuleFolder(string path)
    {
        if (_entries is null)
            return null;
        var nearest = SelfAndAncestors(path).First(_entries.ContainsKey);
        return nearest.Length > 0 || _ruleAtRoot ? nearest : null;
    }

    /// <summary>
    /// The direct children of <paramref name="folder"/> whose access differs from the folder's, by name: those a rule
    /// names, or that lead to a rule. Every other child has the folder's own access.
    /// </summary>
    public IReadOnlyDictionary<string, FolderAccess> ChildrenWithRules(string folder, IReadOnlySet<string> granted)
    {
        if (_entries is null)
            return new Dictionary<string, FolderAccess>();
        var children = new Dictionary<string, FolderAccess>(_comparer);
        foreach (var path in _entries.Keys.Where(p => IsBelow(p, folder, _comparison)))
        {
            var name = path[(folder.Length == 0 ? 0 : folder.Length + 1)..].Split('/')[0];
            if (!children.ContainsKey(name))
                children[name] = At(folder.Length == 0 ? name : folder + "/" + name, granted);
        }
        return children;
    }

    /// <summary>
    /// The subject may use <paramref name="right"/> at <paramref name="folder"/> and everywhere below it: the check
    /// before changing a whole tree (deleting or moving a folder), so that nobody changes what they can't see or
    /// change.
    /// </summary>
    public bool AllowsThroughout(string folder, string right, IReadOnlySet<string> granted) =>
        At(folder, granted).Has(right)
        && (
            _entries is null
            || _entries.Where(e => IsBelow(e.Key, folder, _comparison)).All(e => e.Value.Contains(right))
        );

    /// <summary>Where the subject may use <paramref name="right"/>, for the queries over the index.</summary>
    public PathFilter FilterFor(string right, IReadOnlySet<string> granted)
    {
        if (!granted.Contains(right))
            return PathFilter.Nothing;
        if (_entries is null)
            return PathFilter.Everything;

        // Parents first: keep an entry only where it changes whether the right applies, so that kept entries
        // alternate down the tree (allowed, then not allowed below it, then allowed again...).
        var kept = new Dictionary<string, (bool Allowed, string? Parent)>(_comparer);
        foreach (var (path, rights) in _entries.OrderBy(e => Depth(e.Key)))
        {
            var allowed = rights.Contains(right);
            var parent = Ancestors(path).FirstOrDefault(kept.ContainsKey);
            if (parent is null || kept[parent].Allowed != allowed)
                kept[path] = (allowed, parent);
        }

        var regions = kept.Where(k => k.Value.Allowed)
            .Select(k => new PathRegion(
                k.Key,
                [.. kept.Where(c => c.Value.Parent is { } parent && _comparer.Equals(parent, k.Key)).Select(c => c.Key)]
            ))
            .ToList();
        return new PathFilter(regions);
    }

    /// <summary>
    /// The level a principal with <paramref name="rules"/> has at <paramref name="path"/>; null when it has no say.
    /// </summary>
    public static FolderLevel? LevelAt(
        IReadOnlyDictionary<string, FolderLevel> rules,
        string path,
        bool unrestrictedByDefault
    )
    {
        foreach (var candidate in SelfAndAncestors(path))
        {
            if (rules.TryGetValue(candidate, out var level))
                return level;
        }
        return unrestrictedByDefault ? FolderLevel.Write : null;
    }

    /// <summary>
    /// <paramref name="path"/> is <paramref name="folder"/> or below it (root-relative paths, host case rules).
    /// </summary>
    public static bool IsSameOrBelow(string path, string folder) =>
        folder.Length == 0 || LibraryPaths.PathComparer.Equals(path, folder) || IsBelow(path, folder);

    /// <summary><paramref name="path"/> is strictly below <paramref name="folder"/>.</summary>
    public static bool IsBelow(string path, string folder) => IsBelow(path, folder, LibraryPaths.PathComparison);

    private static bool IsBelow(string path, string folder, StringComparison comparison) =>
        folder.Length == 0
            ? path.Length > 0
            : path.Length > folder.Length + 1 && path[folder.Length] == '/' && path.StartsWith(folder, comparison);

    /// <summary>The path, then each folder above it up to the root (<c>a/b</c>, <c>a</c>, empty).</summary>
    public static IEnumerable<string> SelfAndAncestors(string path)
    {
        yield return path;
        foreach (var ancestor in Ancestors(path))
            yield return ancestor;
    }

    private static IEnumerable<string> Ancestors(string path)
    {
        while (path.Length > 0)
        {
            var slash = path.LastIndexOf('/');
            path = slash < 0 ? string.Empty : path[..slash];
            yield return path;
        }
    }

    private static int Depth(string path) => path.Length == 0 ? 0 : path.Count(c => c == '/') + 1;

    private IReadOnlySet<string> Nearest(string path) =>
        SelfAndAncestors(path).Select(p => _entries!.GetValueOrDefault(p)).First(r => r is not null)!;

    /// <summary>
    /// The subject can't browse the folder, but can browse something below it: the folder only leads there.
    /// </summary>
    private bool IsPathOnly(string path, IReadOnlySet<string> rights, IReadOnlySet<string> granted) =>
        granted.Contains(Rights.Browse)
        && !rights.Contains(Rights.Browse)
        && _entries!.Any(e => IsBelow(e.Key, path, _comparison) && e.Value.Contains(Rights.Browse));

    private static HashSet<string> RightsAt(IReadOnlyList<FolderPrincipal> principals, string path)
    {
        var rights = new HashSet<string>(StringComparer.Ordinal);
        foreach (var principal in principals)
        {
            var level = principal.DefaultLevel is { } fallback
                ? LevelAt(principal.Rules, path, false) ?? fallback
                : LevelAt(principal.Rules, path, principal.UnrestrictedByDefault);
            foreach (var right in principal.Rights)
            {
                if (RightCatalog.Find(right) is not { } definition)
                    continue;
                if (definition.AtFolder is not { } needed || level >= needed)
                    rights.Add(right);
            }
        }
        return rights;
    }
}
