using System.Linq.Expressions;
using Sfe.Core.Paths;
using Sfe.Core.Search;

namespace Sfe.Core.Access;

/// <summary>
/// One principal's part in a subject's folder access: a group the subject is in, or the subject's own account.
/// </summary>
/// <param name="Rules">Its folder rules by root-relative path.</param>
/// <param name="Rights">The rights it grants: a group's own; for an account, those of all its groups in the library.</param>
/// <param name="UnrestrictedByDefault">
/// True for a group: where no rule of it is above a path, it has <see cref="FolderLevel.Write"/> there (what every
/// group had before folder rules). False for an account: without a rule of its own it has no say.
/// </param>
public sealed record FolderPrincipal(IReadOnlyDictionary<string, FolderLevel> Rules, IReadOnlySet<string> Rights, bool UnrestrictedByDefault);

/// <summary>
/// Where a subject may use its rights in a library, compiled from the folder rules of its principals
/// (agents/docs/features/folder-access.md). For each principal the nearest rule at or above a path decides its level
/// there; a right whose <see cref="RightDefinition.AtFolder"/> that level reaches is granted by that principal; the
/// subject has the union of what its principals grant. Rights of the whole space (no <c>AtFolder</c>) apply everywhere.
/// </summary>
/// <remarks>
/// The table holds the root and every path some principal has a rule on, each with the subject's rights there. It is
/// exact: no principal has a rule between a path and the deepest table entry above it, so every principal's nearest
/// rule, hence the rights, are the same for both. Without any rule the policy is <see cref="Unrestricted"/>.
/// </remarks>
public sealed class FolderPolicy
{
    /// <summary>No folder rule applies: the subject's rights apply everywhere.</summary>
    public static FolderPolicy Unrestricted { get; } = new(null);

    /// <summary>Rights by path (<see cref="LibraryPaths.PathComparer"/>); null when unrestricted.</summary>
    private readonly Dictionary<string, IReadOnlySet<string>>? _entries;

    private FolderPolicy(Dictionary<string, IReadOnlySet<string>>? entries) => _entries = entries;

    public bool IsUnrestricted => _entries is null;

    /// <summary>The paths with a rule of some principal, with the root: where what the subject may do can change.</summary>
    public IEnumerable<string> Paths => _entries is null ? [string.Empty] : _entries.Keys;

    public static FolderPolicy Compile(IReadOnlyList<FolderPrincipal> principals)
    {
        if (principals.All(p => p.Rules.Count == 0))
            return Unrestricted;

        var normalized = principals.Select(p => p with { Rules = new Dictionary<string, FolderLevel>(p.Rules, LibraryPaths.PathComparer) }).ToList();
        var paths = new HashSet<string>(LibraryPaths.PathComparer) { string.Empty };
        foreach (var principal in normalized)
            paths.UnionWith(principal.Rules.Keys);
        return new FolderPolicy(paths.ToDictionary(path => path, IReadOnlySet<string> (path) => RightsAt(normalized, path), LibraryPaths.PathComparer));
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
    /// The direct children of <paramref name="folder"/> whose access differs from the folder's, by name: those a rule
    /// names, or that lead to a rule. Every other child has the folder's own access.
    /// </summary>
    public IReadOnlyDictionary<string, FolderAccess> ChildrenWithRules(string folder, IReadOnlySet<string> granted)
    {
        if (_entries is null)
            return new Dictionary<string, FolderAccess>();
        var children = new Dictionary<string, FolderAccess>(LibraryPaths.PathComparer);
        foreach (var path in _entries.Keys.Where(p => IsBelow(p, folder)))
        {
            var name = path[(folder.Length == 0 ? 0 : folder.Length + 1)..].Split('/')[0];
            if (!children.ContainsKey(name))
                children[name] = At(folder.Length == 0 ? name : folder + "/" + name, granted);
        }
        return children;
    }

    /// <summary>
    /// The subject may use <paramref name="right"/> at <paramref name="folder"/> and everywhere below it: the check
    /// before changing a whole tree (deleting or moving a folder), so that nobody changes what they can't see or change.
    /// </summary>
    public bool AllowsThroughout(string folder, string right, IReadOnlySet<string> granted) =>
        At(folder, granted).Has(right)
        && (_entries is null || _entries.Where(e => IsBelow(e.Key, folder)).All(e => e.Value.Contains(right)));

    /// <summary>Where the subject may use <paramref name="right"/>, for the queries over the index.</summary>
    public PathFilter FilterFor(string right, IReadOnlySet<string> granted)
    {
        if (!granted.Contains(right))
            return PathFilter.Nothing;
        if (_entries is null)
            return PathFilter.Everything;

        // Parents first: keep an entry only where it changes whether the right applies, so that kept entries
        // alternate down the tree (allowed, then not allowed below it, then allowed again...).
        var kept = new Dictionary<string, (bool Allowed, string? Parent)>(LibraryPaths.PathComparer);
        foreach (var (path, rights) in _entries.OrderBy(e => Depth(e.Key)))
        {
            var allowed = rights.Contains(right);
            var parent = Ancestors(path).FirstOrDefault(kept.ContainsKey);
            if (parent is null || kept[parent].Allowed != allowed)
                kept[path] = (allowed, parent);
        }

        var regions = kept.Where(k => k.Value.Allowed)
            .Select(k => new PathRegion(k.Key, [.. kept.Where(c => c.Value.Parent is { } parent && LibraryPaths.PathComparer.Equals(parent, k.Key)).Select(c => c.Key)]))
            .ToList();
        return new PathFilter(regions);
    }

    /// <summary>The level a principal with <paramref name="rules"/> has at <paramref name="path"/>; null when it has no say.</summary>
    public static FolderLevel? LevelAt(IReadOnlyDictionary<string, FolderLevel> rules, string path, bool unrestrictedByDefault)
    {
        foreach (var candidate in SelfAndAncestors(path))
        {
            if (rules.TryGetValue(candidate, out var level))
                return level;
        }
        return unrestrictedByDefault ? FolderLevel.Write : null;
    }

    /// <summary><paramref name="path"/> is <paramref name="folder"/> or below it (root-relative paths, host case rules).</summary>
    public static bool IsSameOrBelow(string path, string folder) =>
        folder.Length == 0 || LibraryPaths.PathComparer.Equals(path, folder) || IsBelow(path, folder);

    /// <summary><paramref name="path"/> is strictly below <paramref name="folder"/>.</summary>
    public static bool IsBelow(string path, string folder) =>
        folder.Length == 0
            ? path.Length > 0
            : path.Length > folder.Length + 1 && path[folder.Length] == '/' && path.StartsWith(folder, LibraryPaths.PathComparison);

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

    /// <summary>The subject can't browse the folder, but can browse something below it: the folder only leads there.</summary>
    private bool IsPathOnly(string path, IReadOnlySet<string> rights, IReadOnlySet<string> granted) =>
        granted.Contains(Rights.Browse)
        && !rights.Contains(Rights.Browse)
        && _entries!.Any(e => IsBelow(e.Key, path) && e.Value.Contains(Rights.Browse));

    private static HashSet<string> RightsAt(IReadOnlyList<FolderPrincipal> principals, string path)
    {
        var rights = new HashSet<string>(StringComparer.Ordinal);
        foreach (var principal in principals)
        {
            var level = LevelAt(principal.Rules, path, principal.UnrestrictedByDefault);
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

/// <summary>What a subject may do at one path: the rights it was granted in the library that the folder lets through.</summary>
public sealed class FolderAccess
{
    private readonly IReadOnlySet<string> _granted;
    private readonly IReadOnlySet<string>? _atFolder;

    internal FolderAccess(IReadOnlySet<string> granted, IReadOnlySet<string>? atFolder, bool pathOnly)
    {
        _granted = granted;
        _atFolder = atFolder;
        PathOnly = pathOnly;
    }

    /// <summary>
    /// The subject can't browse the folder but can browse something below it: the folder is shown only to lead there,
    /// with nothing of its own (no files, counts, covers).
    /// </summary>
    public bool PathOnly { get; }

    /// <summary>The folder is shown to the subject, browsable or only leading to what is.</summary>
    public bool IsVisible => Has(Rights.Browse) || PathOnly;

    public bool Has(string right) => _granted.Contains(right) && (_atFolder is null || _atFolder.Contains(right));

    /// <summary>What both allow: one place reached by two paths (a link and its target) gets the stricter access.</summary>
    public FolderAccess And(FolderAccess other) =>
        _atFolder is null && other._atFolder is null
            ? this
            : new FolderAccess(_granted, Allowed.Where(other.Has).ToHashSet(), PathOnly && other.PathOnly);

    /// <summary>The rights the subject has here.</summary>
    public IEnumerable<string> Allowed => _atFolder is null ? _granted : _granted.Where(_atFolder.Contains);
}

/// <param name="Path">A folder where the right applies, with everything below it...</param>
/// <param name="Except">...but these folders below it (and what is below them).</param>
public sealed record PathRegion(string Path, IReadOnlyList<string> Except);

/// <summary>
/// The paths where a right applies (<see cref="FolderPolicy.FilterFor"/>): a few folders, each less some folders below
/// it. Turned into an index predicate by <see cref="ToPredicate{T}"/>: LIKE prefixes on full paths, as many as the
/// rules (at most <see cref="FolderRuleService.MaxRules"/>).
/// </summary>
public sealed class PathFilter
{
    public static PathFilter Everything { get; } = new([new PathRegion(string.Empty, [])]);
    public static PathFilter Nothing { get; } = new([]);

    public PathFilter(IReadOnlyList<PathRegion> regions) => Regions = regions;

    public IReadOnlyList<PathRegion> Regions { get; }

    public bool IsEverything => Regions is [{ Path.Length: 0, Except.Count: 0 }];

    /// <summary>Equal for equal filters: what results computed with it can be cached by.</summary>
    public string Key => string.Join('|', Regions.Select(r => r.Path + " " + string.Join(' ', r.Except)));
    public bool IsNothing => Regions.Count == 0;

    /// <summary>The right applies at <paramref name="path"/> (root-relative).</summary>
    public bool Includes(string path) =>
        Regions.Any(r => FolderPolicy.IsSameOrBelow(path, r.Path) && !r.Except.Any(e => FolderPolicy.IsSameOrBelow(path, e)));

    /// <summary>
    /// Index rows whose full path (selected by <paramref name="fullName"/>) lies where the right applies, in the
    /// library of <paramref name="paths"/>.
    /// </summary>
    public Expression<Func<T, bool>> ToPredicate<T>(Expression<Func<T, string>> fullName, LibraryPaths paths)
    {
        // Every InTree shares the parameter of fullName: their bodies combine as they are.
        Expression body = Expression.Constant(false);
        foreach (var region in Regions)
        {
            Expression inRegion = SearchPredicateBuilder.InTree(fullName, paths.Resolve(region.Path)).Body;
            foreach (var except in region.Except)
                inRegion = Expression.AndAlso(inRegion, Expression.Not(SearchPredicateBuilder.InTree(fullName, paths.Resolve(except)).Body));
            body = body is ConstantExpression ? inRegion : Expression.OrElse(body, inRegion);
        }
        return Expression.Lambda<Func<T, bool>>(body, fullName.Parameters);
    }
}

public static class PathFilterQueries
{
    /// <summary>
    /// The rows whose full path (selected by <paramref name="fullName"/>) <paramref name="filter"/> includes; all of them
    /// for a null filter or <see cref="PathFilter.Everything"/> (no condition added: the query stays as it was).
    /// </summary>
    public static IQueryable<T> WhereVisible<T>(this IQueryable<T> query, PathFilter? filter, Expression<Func<T, string>> fullName, LibraryPaths paths) =>
        filter is null || filter.IsEverything ? query : query.Where(filter.ToPredicate(fullName, paths));
}
