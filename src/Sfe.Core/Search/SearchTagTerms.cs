using Otools.Net.Sfe.Core.Tagging;

namespace Otools.Net.Sfe.Core.Search;

/// <summary>The tags a search names, resolved in the library's tag graph, and the hidden tags it leaves out.</summary>
public sealed class SearchTagTerms
{
    private readonly Dictionary<string, IReadOnlyCollection<int>> _resolved;

    private SearchTagTerms(Dictionary<string, IReadOnlyCollection<int>> resolved, List<string> wanted, int[]? hidden)
    {
        _resolved = resolved;
        Wanted = wanted;
        HiddenIds = hidden;
    }

    /// <summary>The tag names the search asks for (not excluded ones).</summary>
    public IReadOnlyList<string> Wanted { get; }

    /// <summary>
    /// The hidden tags (Archived) whose files are left out: the search names none of them; null when nothing is left
    /// out.
    /// </summary>
    public int[]? HiddenIds { get; }

    /// <summary>The ids a tag term refers to: the tags it names and every tag inheriting from them.</summary>
    public IReadOnlyCollection<int> Resolve(string name) => _resolved[name];

    public static SearchTagTerms Of(SearchQuery parsed, TagGraph graph)
    {
        // A tag term also matches every tag inheriting from it (TagStudio's "substitution").
        var resolved = parsed.TagNames.ToDictionary(
            name => name,
            name => (IReadOnlyCollection<int>)graph.Resolve(name).SelectMany(graph.SelfAndDescendants).ToHashSet(),
            StringComparer.CurrentCultureIgnoreCase
        );

        // Files with a hidden tag (e.g. "Archived") only show up when the search asks for one of those tags.
        var hidden = graph.Nodes.Where(n => n.IsHidden).Select(n => n.Id).ToHashSet();
        var wanted = parsed
            .Groups.SelectMany(g => g.Tags)
            .Where(t => t is { Name: not null, Negated: false })
            .Select(t => t.Name!)
            .ToList();
        var requested = wanted.SelectMany(name => resolved[name]);
        var left = hidden.Count > 0 && !requested.Any(hidden.Contains) ? hidden.ToArray() : null;
        return new SearchTagTerms(resolved, wanted, left);
    }
}
