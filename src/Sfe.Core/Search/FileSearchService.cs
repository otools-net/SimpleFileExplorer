using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Otools.Net.Sfe.Core.Access;
using Otools.Net.Sfe.Core.Data;
using Otools.Net.Sfe.Core.Library;
using Otools.Net.Sfe.Core.Paths;
using Otools.Net.Sfe.Core.Tagging;

namespace Otools.Net.Sfe.Core.Search;

/// <summary>Searches the file index.</summary>
public sealed class FileSearchService(
    IDbContextFactory<SfeDbContext> dbFactory,
    LibraryPaths paths,
    MediaClassifier classifier,
    TagService tags,
    SearchTermSettings settings
)
{
    /// <summary>
    /// The first page counts the matches (for the listing's progress), up to this many: a count stops reading once it
    /// gets there instead of going through every match of a broad search over millions of files.
    /// </summary>
    public const int CountLimit = 10_000;

    private static readonly Expression<Func<FileEntry, Row>> ToRow = f => new Row(
        f.FullName,
        f.Name,
        f.Extension,
        f.Size,
        f.LastWriteTime,
        f.Width,
        f.Height,
        f.Duration,
        f.Pixels,
        f.TagLinks.Select(l => l.TagId).ToList()
    );

    /// <param name="after">
    /// Cursor of the previous page (<see cref="SearchResults.Next"/>) for the same sort; null for the first page.
    /// </param>
    /// <param name="sort">
    /// <see cref="SortField.Path"/> (the default) groups the results by folder; <see cref="SortField.Name"/> sorts by
    /// file name without extension. Ties are broken by ascending path in both directions, as when browsing.
    /// <see cref="SortField.Duration"/> and <see cref="SortField.Resolution"/> (width × height) list the files whose
    /// value is known first, then the others by path, in both directions.
    /// </param>
    /// <param name="visible">Where the asker may see files (folder rules); null = everywhere.</param>
    /// <returns>null when <paramref name="relativeDirectory"/> or <paramref name="after"/> is not valid.</returns>
    /// <remarks>
    /// Results are paged by key (<see cref="SearchPages"/>). The cursor of the path sort is the last path; the other
    /// sorts put the last value before it.
    /// </remarks>
    public async Task<SearchResults?> SearchAsync(
        string? query,
        string? relativeDirectory,
        string? after,
        int limit,
        SortField sort = SortField.Path,
        bool descending = false,
        CancellationToken cancellationToken = default,
        PathFilter? visible = null
    )
    {
        if (!paths.TryResolve(relativeDirectory, out var directory))
            return null;
        SearchCursor? cursor = null;
        if (after is not null && (cursor = ParseCursor(after, sort)) is null)
            return null;

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var parsed = SearchQuery.Parse(query);
        var graph = await TagGraph.LoadAsync(db, cancellationToken);
        var terms = SearchTagTerms.Of(parsed, graph);
        var files = Matches(db, parsed, terms, directory).WhereVisible(visible, f => f.FullName, paths);

        // The tags the search names (not their descendants) become the "last searched" ones. Next pages of the same
        // search don't count again, nor count the matches again.
        int? total = null;
        if (cursor is null)
        {
            await tags.RecordSearchedAsync(terms.Wanted.SelectMany(graph.Resolve).ToHashSet(), cancellationToken);
            // Unordered on purpose (see SfeDatabase.Configure): ordering would sort every match just to count them.
            total = await files.Take(CountLimit + 1).CountAsync(cancellationToken);
        }

        // Fetch one extra row to know whether another page exists without a COUNT query.
        var ask = new SearchPageAsk(sort, descending, cursor, limit + 1);
        var rows = await SearchPages.ReadAsync(files, ask, ToRow, cancellationToken);

        var items = new List<LibraryItem>(Math.Min(rows.Count, limit));
        foreach (var row in rows.Take(limit))
        {
            if (paths.ToRelative(row.FullName) is { } path)
                items.Add(ToItem(row, path, graph));
        }

        string? next = null;
        if (rows.Count > limit && paths.ToRelative(rows[limit - 1].FullName) is { } lastPath)
            next = SearchCursor.Of(sort, rows[limit - 1], lastPath);

        return new SearchResults(
            Query: query ?? string.Empty,
            Path: paths.ToRelative(directory) ?? string.Empty,
            Items: items,
            Limit: limit,
            HasMore: next is not null,
            Next: next,
            Total: total is null ? null : Math.Min(total.Value, CountLimit),
            TotalCapped: total > CountLimit
        );
    }

    /// <summary>
    /// The indexed files a search matches in <paramref name="directory"/> and below, unordered: what a search lists,
    /// and what tagging the results of a search changes (<see cref="ScopeTagger"/>).
    /// </summary>
    public IQueryable<FileEntry> Matches(
        SfeDbContext db,
        TagGraph graph,
        string? query,
        string directory,
        PathFilter? visible = null
    )
    {
        var parsed = SearchQuery.Parse(query);
        return Matches(db, parsed, SearchTagTerms.Of(parsed, graph), directory)
            .WhereVisible(visible, f => f.FullName, paths);
    }

    private IQueryable<FileEntry> Matches(SfeDbContext db, SearchQuery parsed, SearchTagTerms terms, string directory)
    {
        var predicate = SearchPredicateBuilder.Build<FileEntry>(
            parsed,
            terms.Resolve,
            settings.HighResolution(),
            settings.MediaKinds()
        );
        var files = db
            .Files.AsNoTracking()
            .Where(predicate)
            .Where(SearchPredicateBuilder.InTree<FileEntry>(f => f.FullName, directory));

        if (terms.HiddenIds is { } hiddenIds)
            files = files.Where(f => !f.TagLinks.Any(l => hiddenIds.Contains(l.TagId)));
        return files;
    }

    private LibraryItem ToItem(Row row, string path, TagGraph graph)
    {
        var slash = path.LastIndexOf('/');
        var sized = row.Width > 0 && row.Height > 0;
        return new LibraryItem(
            Name: Path.GetFileName(row.FullName),
            Path: path,
            Kind: classifier.Classify(row.Extension),
            Extension: LibraryBrowser.NormalizeExtension(row.Extension),
            Size: row.Size,
            Modified: new DateTimeOffset(DateTime.SpecifyKind(row.LastWriteTime, DateTimeKind.Local)),
            Directory: slash < 0 ? string.Empty : path[..slash],
            Tags: TagService.Summaries(graph, row.TagIds),
            Width: sized ? row.Width : null,
            Height: sized ? row.Height : null,
            Duration: row.Duration > 0 ? row.Duration : null
        );
    }

    private SearchCursor? ParseCursor(string after, SortField sort) =>
        SearchCursor.Parse(
            after,
            sort,
            path => paths.TryResolve(path, out var fullName) ? fullName : null,
            FileEntry.SortNameMayBeMissing
        );

    private sealed record Row(
        string FullName,
        string Name,
        string Extension,
        long Size,
        DateTime LastWriteTime,
        int? Width,
        int? Height,
        double? Duration,
        long? Pixels,
        List<int> TagIds
    ) : ISearchRow
    {
        string? ISearchRow.SortName => Name;
    }
}
