using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Otools.Net.Sfe.Core.Data;
using Otools.Net.Sfe.Core.Library;
using Otools.Net.Sfe.Core.Search;
using Otools.Net.Sfe.Core.Tagging;

namespace Otools.Net.Sfe.Core.Connections.Indexing;

/// <summary>
/// Searches a connection's files in the library's index, in the library's query language
/// (<see cref="SearchPredicateBuilder"/>), with its sorts, key paging (<see cref="SearchPages"/>) and match count. No
/// provider call: what is found is what the index holds (<see cref="RemoteSearchCoverage"/>).
/// </summary>
/// <remarks>
/// Rows marked removed are never found. Folder rules are conditions of the queries (<see cref="RemoteSearchScope"/>),
/// so counts and later pages never hold a hidden file; the route checks each item shown again. Names sort by their
/// natural key (<see cref="RemoteFile.NameKey"/>), as the listings; rows without one yet come after the others, by
/// their key (lowered path). Width, height and duration are mostly unknown on these rows: <c>w:</c>, <c>h:</c>,
/// <c>r:</c> and <c>d:</c> don't match them.
/// </remarks>
public sealed class RemoteIndexSearch(
    RemoteIndexTransactions store,
    MediaClassifier classifier,
    TagService tags,
    SearchTermSettings settings,
    TimeProvider clock
)
{
    private static readonly Expression<Func<RemoteFile, RemoteSearchRow>> ToRow = f => new RemoteSearchRow(
        f.FullName,
        f.DisplayPath,
        f.ProviderId,
        f.NameKey,
        f.Extension,
        f.Size,
        f.LastWriteTime,
        f.Width,
        f.Height,
        f.Duration,
        f.Pixels,
        f.TagLinks.Select(l => l.TagId).ToList()
    );

    /// <returns>null when <see cref="RemoteSearchAsk.After"/> is not a cursor of the sort.</returns>
    public async Task<RemoteSearchPage?> SearchAsync(RemoteSearchAsk ask, CancellationToken cancellationToken)
    {
        var caseInsensitive = ask.Target.CaseInsensitive;
        SearchCursor? cursor = null;
        if (ask.After is not null && (cursor = ParseCursor(ask.After, ask.Sort, caseInsensitive)) is null)
            return null;

        await using var db = await store.ContextAsync(cancellationToken);
        var coverage = await CoverageAsync(db, ask.Target, cancellationToken);
        if (coverage == RemoteSearchCoverage.None)
            return new RemoteSearchPage(Empty(ask, cursor is null ? 0 : null), [], coverage);

        var parsed = SearchQuery.Parse(ask.Query);
        var graph = await TagGraph.LoadAsync(db, cancellationToken);
        var terms = SearchTagTerms.Of(parsed, graph);
        var files = await MatchesAsync(db, ask, parsed, terms, cancellationToken);

        int? total = null;
        if (cursor is null)
        {
            await tags.RecordSearchedAsync(terms.Wanted.SelectMany(graph.Resolve).ToHashSet(), cancellationToken);
            // Unordered on purpose, as the library's (FileSearchService).
            total = await files.Take(FileSearchService.CountLimit + 1).CountAsync(cancellationToken);
        }

        var pageAsk = new SearchPageAsk(ask.Sort, ask.Descending, cursor, ask.Limit + 1);
        var rows = await SearchPages.ReadAsync(files, pageAsk, ToRow, cancellationToken);
        var shown = rows.Take(ask.Limit).ToList();
        var next = rows.Count > ask.Limit ? SearchCursor.Of(ask.Sort, shown[^1], shown[^1].DisplayPath) : null;
        var results = Empty(ask, total) with
        {
            Items = [.. shown.Select(row => ToItem(row, graph))],
            HasMore = next is not null,
            Next = next,
        };
        return new RemoteSearchPage(results, [.. shown.Select(row => row.ProviderId)], coverage);
    }

    /// <summary>
    /// Every row the search of <paramref name="ask"/> finds (no page, no sort): what a search's tagging changes.
    /// </summary>
    internal async Task<IQueryable<RemoteFile>> MatchesAsync(
        SfeDbContext db,
        RemoteSearchAsk ask,
        CancellationToken cancellationToken
    )
    {
        var parsed = SearchQuery.Parse(ask.Query);
        var graph = await TagGraph.LoadAsync(db, cancellationToken);
        return await MatchesAsync(db, ask, parsed, SearchTagTerms.Of(parsed, graph), cancellationToken);
    }

    private async Task<IQueryable<RemoteFile>> MatchesAsync(
        SfeDbContext db,
        RemoteSearchAsk ask,
        SearchQuery parsed,
        SearchTagTerms terms,
        CancellationToken cancellationToken
    )
    {
        var id = ask.Target.ConnectionId;
        var scope = new RemoteSearchScope(ask.Target.CaseInsensitive, db.Database.IsSqlite());
        var predicate = SearchPredicateBuilder.Build<RemoteFile>(
            parsed,
            terms.Resolve,
            settings.HighResolution(),
            settings.MediaKinds()
        );
        var tagTerms = new RemoteTagTerms(db);
        var files = db
            .RemoteFiles.AsNoTracking()
            .Where(f => f.ConnectionId == id && !f.Removed)
            .Where(tagTerms.Rewrite(predicate))
            .Where(scope.Below(ask.Folder));
        if (terms.HiddenIds is { } hiddenIds)
            files = files.Where(tagTerms.Rewrite(f => !f.TagLinks.Any(l => hiddenIds.Contains(l.TagId))));
        if (ask.Visible is { IsEverything: false } visible)
            files = files.Where(scope.Visible(visible));
        return await scope.WithoutHiddenAsync(db, id, files, ask.HiddenIds, cancellationToken);
    }

    /// <summary>
    /// How much of the connection the index holds: of its root and account, past its listing, recent.
    /// </summary>
    private async Task<RemoteSearchCoverage> CoverageAsync(
        SfeDbContext db,
        ConnectionIndexTarget target,
        CancellationToken cancellationToken
    )
    {
        var id = target.ConnectionId;
        var state = await db
            .RemoteIndexes.AsNoTracking()
            .FirstOrDefaultAsync(s => s.ConnectionId == id, cancellationToken);
        if (state is null || state.RootPath != target.RootPath || state.AccountKey != target.AccountKey)
            return RemoteSearchCoverage.None;
        var recent =
            state.LastSync is { } synced
            && clock.GetUtcNow() - new DateTimeOffset(synced, TimeSpan.Zero) <= RemoteIndexListings.MaxAge;
        return state.Phase == RemoteIndexPhase.Following && recent
            ? RemoteSearchCoverage.Complete
            : RemoteSearchCoverage.Partial;
    }

    private static SearchCursor? ParseCursor(string after, SortField sort, bool caseInsensitive) =>
        SearchCursor.Parse(
            after,
            sort,
            path =>
                RemotePaths.Normalize(path) is { Length: > 0 } normal
                    ? RemoteIndexKeys.Key(normal, caseInsensitive)
                    : null,
            RemoteFile.SortNameMayBeMissing
        );

    private static SearchResults Empty(RemoteSearchAsk ask, int? total) =>
        new(
            Query: ask.Query ?? string.Empty,
            Path: ask.Folder,
            Items: [],
            Limit: ask.Limit,
            HasMore: false,
            Next: null,
            Total: total is null ? null : Math.Min(total.Value, FileSearchService.CountLimit),
            TotalCapped: total > FileSearchService.CountLimit
        );

    private LibraryItem ToItem(RemoteSearchRow row, TagGraph graph)
    {
        var sized = row.Width > 0 && row.Height > 0;
        DateTimeOffset? modified =
            row.LastWriteTime == default ? null : new DateTimeOffset(row.LastWriteTime, TimeSpan.Zero);
        return new LibraryItem(
            Name: RemotePaths.NameOf(row.DisplayPath),
            Path: row.DisplayPath,
            Kind: classifier.Classify(row.Extension),
            Extension: LibraryBrowser.NormalizeExtension(row.Extension),
            Size: row.Size,
            Modified: modified,
            Directory: RemotePaths.Parent(row.DisplayPath),
            Tags: TagService.Summaries(graph, row.TagIds),
            Width: sized ? row.Width : null,
            Height: sized ? row.Height : null,
            Duration: row.Duration > 0 ? row.Duration : null
        );
    }
}
