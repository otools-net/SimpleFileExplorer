using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Sfe.Core.Data;
using Microsoft.Extensions.Options;
using Sfe.Core.Library;
using Sfe.Core.Options;
using Sfe.Core.Paths;
using Sfe.Core.Settings;
using Sfe.Core.Tagging;

namespace Sfe.Core.Search;

/// <summary>Searches the file index.</summary>
public sealed class FileSearchService(
    IDbContextFactory<SfeDbContext> dbFactory, LibraryPaths paths, MediaClassifier classifier, TagService tags, IOptionsMonitor<SfeOptions> options)
{
    /// <summary>
    /// The first page counts the matches (for the listing's progress), up to this many: a count stops reading once it
    /// gets there instead of going through every match of a broad search over millions of files.
    /// </summary>
    public const int CountLimit = 10_000;

    /// <param name="after">
    /// Cursor of the previous page (<see cref="SearchResults.Next"/>) for the same sort; null for the first page.
    /// </param>
    /// <param name="sort">
    /// <see cref="SortField.Path"/> (the default) groups the results by folder; <see cref="SortField.Name"/> sorts by
    /// file name without extension. Ties are broken by ascending path in both directions, as when browsing.
    /// </param>
    /// <returns>null when <paramref name="relativeDirectory"/> or <paramref name="after"/> is not valid.</returns>
    /// <remarks>
    /// Results are paged by key ("after this sort value and path") rather than by offset: an index on the sort column
    /// gives the order, so a page reads the matches it returns instead of sorting every match and skipping the
    /// previous pages. The cursor of the path sort is the last path; the other sorts put the last value before it.
    /// </remarks>
    public async Task<SearchResults?> SearchAsync(
        string? query,
        string? relativeDirectory,
        string? after,
        int limit,
        SortField sort = SortField.Path,
        bool descending = false,
        CancellationToken cancellationToken = default)
    {
        if (!paths.TryResolve(relativeDirectory, out var directory))
            return null;
        Cursor? cursor = null;
        if (after is not null && (cursor = ParseCursor(after, sort)) is null)
            return null;

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var parsed = SearchQuery.Parse(query);
        var graph = await TagGraph.LoadAsync(db, cancellationToken);
        var files = Matches(db, graph, parsed, directory, out var wanted);

        // The tags the search names (not their descendants) become the "last searched" ones. Next pages of the same
        // search don't count again, nor count the matches again.
        int? total = null;
        if (cursor is null)
        {
            await tags.RecordSearchedAsync(wanted.SelectMany(graph.Resolve).ToHashSet(), cancellationToken);
            // Unordered on purpose (see SfeDatabase.Configure): ordering would sort every match just to count them.
            total = await files.Take(CountLimit + 1).CountAsync(cancellationToken);
        }
        else
        {
            files = After(files, sort, descending, cursor);
        }

        // Fetch one extra row to know whether another page exists without a COUNT query.
        var rows = await Order(files, sort, descending)
            .Take(limit + 1)
            .Select(f => new { f.FullName, f.Name, f.Extension, f.Size, f.LastWriteTime, f.Width, f.Height, f.Duration, TagIds = f.TagLinks.Select(l => l.TagId).ToList() })
            .ToListAsync(cancellationToken);

        var items = new List<LibraryItem>(Math.Min(rows.Count, limit));
        foreach (var row in rows.Take(limit))
        {
            var path = paths.ToRelative(row.FullName);
            if (path is null)
                continue;

            var slash = path.LastIndexOf('/');
            items.Add(new LibraryItem(
                Name: Path.GetFileName(row.FullName),
                Path: path,
                Kind: classifier.Classify(row.Extension),
                Extension: LibraryBrowser.NormalizeExtension(row.Extension),
                Size: row.Size,
                Modified: new DateTimeOffset(DateTime.SpecifyKind(row.LastWriteTime, DateTimeKind.Local)),
                Directory: slash < 0 ? string.Empty : path[..slash],
                Tags: TagService.Summaries(graph, row.TagIds),
                Width: row.Width > 0 && row.Height > 0 ? row.Width : null,
                Height: row.Width > 0 && row.Height > 0 ? row.Height : null,
                Duration: row.Duration > 0 ? row.Duration : null));
        }

        string? next = null;
        if (rows.Count > limit && paths.ToRelative(rows[limit - 1].FullName) is { } lastPath)
        {
            var last = rows[limit - 1];
            next = sort switch
            {
                SortField.Name => $"{last.Name}/{lastPath}",
                SortField.Modified => $"{last.LastWriteTime.Ticks.ToString(CultureInfo.InvariantCulture)}/{lastPath}",
                SortField.Size => $"{last.Size.ToString(CultureInfo.InvariantCulture)}/{lastPath}",
                SortField.Type => $"{last.Extension}/{lastPath}",
                _ => lastPath,
            };
        }

        return new SearchResults(
            Query: query ?? string.Empty,
            Path: paths.ToRelative(directory) ?? string.Empty,
            Items: items,
            Limit: limit,
            HasMore: next is not null,
            Next: next,
            Total: total is null ? null : Math.Min(total.Value, CountLimit),
            TotalCapped: total > CountLimit);
    }

    /// <summary>
    /// The indexed files a search matches in <paramref name="directory"/> and below, unordered: what a search lists,
    /// and what tagging the results of a search changes (<see cref="ScopeTagger"/>).
    /// </summary>
    public IQueryable<FileEntry> Matches(SfeDbContext db, TagGraph graph, string? query, string directory) =>
        Matches(db, graph, SearchQuery.Parse(query), directory, out _);

    /// <summary>The thresholds of <c>r:hr</c> and <c>r:lr</c>: those of the HR/LR marks, current file types.</summary>
    private HighResolution HighResolution()
    {
        var browse = options.CurrentValue.Browse;
        return new HighResolution(
            classifier.Extensions(ItemKind.Image),
            Math.Clamp(browse.ImageHighResolutionPixels, 1, SettingsService.MaxHighResolutionPixels),
            classifier.Extensions(ItemKind.Video),
            Math.Clamp(browse.VideoHighResolutionPixels, 1, SettingsService.MaxHighResolutionPixels));
    }

    /// <param name="wanted">The tag names the search asks for (not excluded ones).</param>
    private IQueryable<FileEntry> Matches(SfeDbContext db, TagGraph graph, SearchQuery parsed, string directory, out List<string> wanted)
    {
        // A tag term also matches every tag inheriting from it (TagStudio's "substitution").
        var resolved = parsed.TagNames.ToDictionary(
            name => name,
            name => (IReadOnlyCollection<int>)graph.Resolve(name).SelectMany(graph.SelfAndDescendants).ToHashSet(),
            StringComparer.CurrentCultureIgnoreCase);

        var files = db.Files
            .AsNoTracking()
            .Where(SearchPredicateBuilder.Build(parsed, name => resolved[name], HighResolution()))
            .Where(SearchPredicateBuilder.InTree<FileEntry>(f => f.FullName, directory));

        // Files with a hidden tag (e.g. "Archived") only show up when the search asks for one of those tags.
        var hidden = graph.Nodes.Where(n => n.IsHidden).Select(n => n.Id).ToHashSet();
        wanted = parsed.Groups.SelectMany(g => g.Tags).Where(t => t is { Name: not null, Negated: false }).Select(t => t.Name!).ToList();
        var requested = wanted.SelectMany(name => resolved[name]);
        if (hidden.Count > 0 && !requested.Any(hidden.Contains))
        {
            var hiddenIds = hidden.ToArray();
            files = files.Where(f => !f.TagLinks.Any(l => hiddenIds.Contains(l.TagId)));
        }

        return files;
    }

    /// <param name="FullName">Physical path of the last file of the previous page.</param>
    /// <param name="Text">Its name or extension (name and type sorts).</param>
    /// <param name="Number">Its size or modification ticks (size and date sorts).</param>
    private sealed record Cursor(string FullName, string? Text = null, long Number = 0);

    /// <summary>
    /// Reads "{value}/{path}", or the bare path of the path sort. The value never holds a '/': names and extensions
    /// are parts of a file name.
    /// </summary>
    private Cursor? ParseCursor(string after, SortField sort)
    {
        var path = after;
        var value = string.Empty;
        if (sort is not SortField.Path)
        {
            var slash = after.IndexOf('/');
            if (slash < 0)
                return null;
            value = after[..slash];
            path = after[(slash + 1)..];
        }

        if (!paths.TryResolve(path, out var fullName))
            return null;

        return sort switch
        {
            SortField.Name or SortField.Type => new Cursor(fullName, value),
            SortField.Size or SortField.Modified =>
                long.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var number)
                && (sort == SortField.Size || number >= 0 && number <= DateTime.MaxValue.Ticks)
                    ? new Cursor(fullName, Number: number)
                    : null,
            _ => new Cursor(fullName),
        };
    }

    /// <summary>By the sort value, ascending or descending, then always by ascending path.</summary>
    private static IQueryable<FileEntry> Order(IQueryable<FileEntry> files, SortField sort, bool descending) => (sort, descending) switch
    {
        (SortField.Name, false) => files.OrderBy(f => f.Name).ThenBy(f => f.FullName),
        (SortField.Name, true) => files.OrderByDescending(f => f.Name).ThenBy(f => f.FullName),
        (SortField.Modified, false) => files.OrderBy(f => f.LastWriteTime).ThenBy(f => f.FullName),
        (SortField.Modified, true) => files.OrderByDescending(f => f.LastWriteTime).ThenBy(f => f.FullName),
        (SortField.Size, false) => files.OrderBy(f => f.Size).ThenBy(f => f.FullName),
        (SortField.Size, true) => files.OrderByDescending(f => f.Size).ThenBy(f => f.FullName),
        (SortField.Type, false) => files.OrderBy(f => f.Extension).ThenBy(f => f.FullName),
        (SortField.Type, true) => files.OrderByDescending(f => f.Extension).ThenBy(f => f.FullName),
        (_, false) => files.OrderBy(f => f.FullName),
        (_, true) => files.OrderByDescending(f => f.FullName),
    };

    /// <summary>The files following the cursor in <see cref="Order"/>.</summary>
    /// <remarks>String comparisons become SQL comparisons with the column's collation, the one ORDER BY uses too.</remarks>
    private static IQueryable<FileEntry> After(IQueryable<FileEntry> files, SortField sort, bool descending, Cursor cursor)
    {
        var path = cursor.FullName;
        var text = cursor.Text;
        var number = cursor.Number;
        var time = sort == SortField.Modified ? new DateTime(number) : default;
        return (sort, descending) switch
        {
            (SortField.Name, false) => files.Where(f => string.Compare(f.Name, text) > 0 || f.Name == text && string.Compare(f.FullName, path) > 0),
            (SortField.Name, true) => files.Where(f => string.Compare(f.Name, text) < 0 || f.Name == text && string.Compare(f.FullName, path) > 0),
            (SortField.Modified, false) => files.Where(f => f.LastWriteTime > time || f.LastWriteTime == time && string.Compare(f.FullName, path) > 0),
            (SortField.Modified, true) => files.Where(f => f.LastWriteTime < time || f.LastWriteTime == time && string.Compare(f.FullName, path) > 0),
            (SortField.Size, false) => files.Where(f => f.Size > number || f.Size == number && string.Compare(f.FullName, path) > 0),
            (SortField.Size, true) => files.Where(f => f.Size < number || f.Size == number && string.Compare(f.FullName, path) > 0),
            (SortField.Type, false) => files.Where(f => string.Compare(f.Extension, text) > 0 || f.Extension == text && string.Compare(f.FullName, path) > 0),
            (SortField.Type, true) => files.Where(f => string.Compare(f.Extension, text) < 0 || f.Extension == text && string.Compare(f.FullName, path) > 0),
            (_, false) => files.Where(f => string.Compare(f.FullName, path) > 0),
            (_, true) => files.Where(f => string.Compare(f.FullName, path) < 0),
        };
    }
}
