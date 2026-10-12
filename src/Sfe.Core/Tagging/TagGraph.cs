using Microsoft.EntityFrameworkCore;
using Sfe.Core.Data;

namespace Sfe.Core.Tagging;

/// <summary>
/// Every tag with its aliases and parent/child links, loaded in one go. A library holds a few thousand tags at
/// most, and walking the hierarchy in memory works the same on every database (TagStudio uses recursive CTEs).
/// </summary>
public sealed class TagGraph
{
    public sealed record Node(
        int Id,
        string Name,
        string? Shorthand,
        string? Color,
        bool IsCategory,
        bool IsHidden,
        int? DisambiguationId,
        IReadOnlyList<string> Aliases,
        List<int> Parents,
        List<int> Children);

    private readonly Dictionary<int, Node> _nodes;

    private TagGraph(Dictionary<int, Node> nodes) => _nodes = nodes;

    public IEnumerable<Node> Nodes => _nodes.Values;

    public static async Task<TagGraph> LoadAsync(SfeDbContext db, CancellationToken cancellationToken = default)
    {
        var tags = await db.Tags.AsNoTracking()
            .Select(t => new { t.Id, t.Name, t.Shorthand, t.Color, t.IsCategory, t.IsHidden, t.DisambiguationId })
            .ToListAsync(cancellationToken);
        var aliases = (await db.TagAliases.AsNoTracking().Select(a => new { a.TagId, a.Name }).ToListAsync(cancellationToken))
            .ToLookup(a => a.TagId, a => a.Name);
        var links = await db.TagParents.AsNoTracking().ToListAsync(cancellationToken);

        var nodes = tags.ToDictionary(t => t.Id, t => new Node(
            t.Id, t.Name, t.Shorthand, t.Color, t.IsCategory, t.IsHidden, t.DisambiguationId,
            aliases[t.Id].ToList(), [], []));
        foreach (var link in links)
        {
            if (nodes.TryGetValue(link.ChildId, out var child) && nodes.TryGetValue(link.ParentId, out var parent))
            {
                child.Parents.Add(parent.Id);
                parent.Children.Add(child.Id);
            }
        }

        return new TagGraph(nodes);
    }

    public bool Contains(int id) => _nodes.ContainsKey(id);

    public Node? Find(int id) => _nodes.GetValueOrDefault(id);

    /// <summary>The tag and every tag inheriting from it, directly or not.</summary>
    public HashSet<int> SelfAndDescendants(int id) => Walk(id, node => node.Children);

    /// <summary>The tag and every tag it inherits from, directly or not.</summary>
    public HashSet<int> SelfAndAncestors(int id) => Walk(id, node => node.Parents);

    private HashSet<int> Walk(int start, Func<Node, List<int>> next)
    {
        var seen = new HashSet<int>();
        if (!_nodes.ContainsKey(start))
            return seen;

        // Cycles are refused on edit, but a hand-edited database could still contain one: never revisit a node.
        var pending = new Stack<int>([start]);
        while (pending.Count > 0)
        {
            var id = pending.Pop();
            if (!seen.Add(id))
                continue;
            foreach (var other in next(_nodes[id]))
                pending.Push(other);
        }

        return seen;
    }

    /// <summary>
    /// Tags a search term names: exact (case-insensitive) match of the name, shorthand or an alias. Underscores
    /// stand for spaces, so <c>t:Tag_Name</c> finds "Tag Name" (TagStudio's escaping). A term never holds double
    /// quotes (they only group words), so names are compared without theirs: <c>t:"12 Vinyl"</c> finds
    /// <c>12" Vinyl</c>, as the client searches for it.
    /// </summary>
    public IReadOnlyList<int> Resolve(string term)
    {
        var spaced = term.Replace('_', ' ');
        return _nodes.Values
            .Where(n => Names(n).Select(Unquoted).Any(name => Same(name, term) || Same(name, spaced)))
            .Select(n => n.Id)
            .ToList();
    }

    private static string Unquoted(string name) => name.Contains('"') ? name.Replace("\"", string.Empty) : name;

    /// <summary>The name, then the shorthand, then the aliases.</summary>
    public static IEnumerable<string> Names(Node node)
    {
        yield return node.Name;
        if (!string.IsNullOrEmpty(node.Shorthand))
            yield return node.Shorthand;
        foreach (var alias in node.Aliases)
            yield return alias;
    }

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.CurrentCultureIgnoreCase);

    /// <summary>"Name (Parent shorthand or name)" when the tag has a disambiguation parent, else the name.</summary>
    public string DisplayName(Node node)
    {
        if (node.DisambiguationId is not { } parentId || !_nodes.TryGetValue(parentId, out var parent))
            return node.Name;
        return $"{node.Name} ({(string.IsNullOrEmpty(parent.Shorthand) ? parent.Name : parent.Shorthand)})";
    }

    public TagSummary Summary(Node node) =>
        new(node.Id, node.Name, DisplayName(node), node.Color, node.IsCategory, node.IsHidden);

    public TagSummary? Summary(int id) => Find(id) is { } node ? Summary(node) : null;

    /// <summary>
    /// Groups tags by category, like TagStudio's inspector: a tag goes under every category tag it inherits from
    /// (and under itself when it is a category); tags without any category go to the default group (null).
    /// </summary>
    public IReadOnlyList<TagGroup> GroupByCategory(IEnumerable<int> tagIds)
    {
        var groups = new Dictionary<int, List<TagSummary>>();
        var uncategorized = new List<TagSummary>();
        foreach (var id in tagIds)
        {
            if (Find(id) is not { } node)
                continue;
            var summary = Summary(node);
            var categories = SelfAndAncestors(id).Where(a => _nodes[a].IsCategory).ToList();
            if (categories.Count == 0)
                uncategorized.Add(summary);
            foreach (var category in categories)
            {
                if (!groups.TryGetValue(category, out var list))
                    groups[category] = list = [];
                list.Add(summary);
            }
        }

        var result = groups
            .Select(g => new TagGroup(Summary(g.Key), Sort(g.Value)))
            .OrderBy(g => g.Category!.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        // TagStudio lists the default group after the categories.
        if (uncategorized.Count > 0)
            result.Add(new TagGroup(null, Sort(uncategorized)));
        return result;
    }

    public static IReadOnlyList<TagSummary> Sort(IEnumerable<TagSummary> tags) =>
        tags.OrderBy(t => t.DisplayName, StringComparer.CurrentCultureIgnoreCase).ThenBy(t => t.Id).ToList();
}
