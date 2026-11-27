using Microsoft.EntityFrameworkCore;
using Otools.Net.Sfe.Core.Data;

namespace Otools.Net.Sfe.Core.Tagging;

/// <summary>
/// The tag lists of the client's sidebar, read from the index (<see cref="TagService.GetSidebarAsync"/>).
/// </summary>
internal static class TagSidebarLists
{
    /// <summary>
    /// Most used tags, last searched tags, tags of the last viewed files, and random tags in none of those lists,
    /// <see cref="TagSidebarChoice.Count"/> of each. A list switched off stays empty and isn't read (no query, no
    /// random pick); the history lists stay empty while history is off.
    /// </summary>
    /// <param name="counts">Files tagged with each tag (those the asker sees).</param>
    public static async Task<TagSidebar> BuildAsync(
        SfeDbContext db,
        TagGraph graph,
        IReadOnlyDictionary<int, int> counts,
        TagSidebarChoice choice,
        CancellationToken cancellationToken
    )
    {
        var count = Math.Max(1, choice.Count);
        var history = choice.History;
        TagUsage Usage(int id) => new(graph.Summary(id)!, counts.GetValueOrDefault(id));

        var popular = choice.Popular ? Popular(graph, counts, count) : [];
        var searched =
            !history || !choice.Searched
                ? []
                : await db
                    .TagActivity.Where(a => a.LastSearchedAt != null)
                    .OrderByDescending(a => a.LastSearchedAt)
                    .Select(a => a.TagId)
                    .Take(count)
                    .ToListAsync(cancellationToken);
        var viewed = !history
            ? []
            : await db
                .TagActivity.Where(a => a.LastViewedAt != null)
                .OrderByDescending(a => a.LastViewedAt)
                .Select(a => a.TagId)
                .Take(count)
                .ToListAsync(cancellationToken);
        var lucky = choice.Lucky ? Lucky(graph, popular.Concat(searched).Concat(viewed).ToHashSet(), count) : [];

        return new TagSidebar(
            popular.Select(Usage).ToList(),
            searched.Where(graph.Contains).Select(Usage).ToList(),
            viewed.Where(graph.Contains).Select(Usage).ToList(),
            lucky.Select(Usage).ToList(),
            history,
            choice.Popular,
            choice.Searched,
            choice.Lucky
        );
    }

    /// <summary>The tags on the most files, by name when as many.</summary>
    private static List<int> Popular(TagGraph graph, IReadOnlyDictionary<int, int> counts, int count) =>
        counts
            .Where(c => c.Value > 0 && graph.Contains(c.Key))
            .OrderByDescending(c => c.Value)
            .ThenBy(c => graph.Find(c.Key)!.Name, StringComparer.CurrentCultureIgnoreCase)
            .Take(count)
            .Select(c => c.Key)
            .ToList();

    /// <summary>Random tags not in <paramref name="shown"/>.</summary>
    private static List<int> Lucky(TagGraph graph, HashSet<int> shown, int count) =>
        graph
            .Nodes.Where(n => !shown.Contains(n.Id))
            .Select(n => n.Id)
            .OrderBy(_ => Random.Shared.Next())
            .Take(count)
            .ToList();
}
