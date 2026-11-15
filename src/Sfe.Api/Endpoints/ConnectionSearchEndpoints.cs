using Otools.Net.Sfe.Api.Security;
using Otools.Net.Sfe.Core.Access;
using Otools.Net.Sfe.Core.Connections;
using Otools.Net.Sfe.Core.Connections.Indexing;
using Otools.Net.Sfe.Core.Library;

namespace Otools.Net.Sfe.Api.Endpoints;

/// <summary>
/// A connection's files searched in the library's index (<see cref="RemoteIndexSearch"/>): the library's search with
/// the connection's id in front, no provider call. Opened as every route on a connection's files
/// (<see cref="ConnectionFileServices.OpenAsync(int, string, CancellationToken)"/>): 404 for a connection the session
/// may not use (an account's own connection is its owner's only) or a folder it doesn't see.
/// </summary>
public static class ConnectionSearchEndpoints
{
    private const string SearchDescription = """
        The search of `GET api/search` (same terms, sorts, `after` paging and `total` on the first page), on the
        library's index of the connection: paths are relative to the connection's root, `path` is the folder searched
        with everything below it. The index holds what the provider tells when listing: width, height and duration
        are mostly unknown, so `w:`, `h:`, `r:` and `d:` rarely match. `coverage` says how much of the connection the
        index holds: `none` (not indexed in this library yet: nothing found, the first sync starts), `partial` (being
        listed, or not synced for a while) or `complete`. Folder rules apply: hidden folders and files are in no page
        nor count. Names sort as the connection's listings sort them; files indexed before that key existed come
        after the others, by path.
        """;

    public static RouteGroupBuilder MapConnectionSearchEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/connections/{id:int}/search", Search)
            .WithTags("Connections")
            .RequireRateLimiting(RequestLimits.Search)
            .RequireRight(Rights.UseConnections)
            .FolderScoped("library.browse; 404 for a connection the subject may not use or a folder it doesn't see")
            // The handler answers problems too (IResult): the page's shape is declared for the document.
            .Produces<ConnectionSearchResults>()
            .WithName("SearchConnection")
            .WithSummary("Searches the library's index of a connection's files.")
            .WithDescription(SearchDescription);
        return api;
    }

    private static async Task<IResult> Search(
        int id,
        [AsParameters] ConnectionFileServices files,
        [AsParameters] ConnectionSearchServices search,
        [AsParameters] LibrarySearchQuery query,
        CancellationToken cancellationToken
    )
    {
        if (RemotePaths.Normalize(query.Path) is not { } path)
            return TypedResults.NotFound();
        var (open, answer) = await files.OpenAsync(id, Rights.Browse, cancellationToken);
        if (open is null)
            return answer!;
        if (open.At(path) is not { IsVisible: true })
            return TypedResults.NotFound();
        if (await files.Connections.FindAsync(id, cancellationToken) is not { } info)
            return TypedResults.NotFound();

        // Searched in this library: its index begins (or catches up when old) in the background.
        await search.Indexes.OpenedAsync(id, cancellationToken);
        var found = await search.Search.SearchAsync(AskOf(query, path, open, info, search), cancellationToken);
        if (found is null)
            return TypedResults.NotFound();
        return TypedResults.Ok(AnswerOf(open, found));
    }

    private static RemoteSearchAsk AskOf(
        LibrarySearchQuery query,
        string path,
        OpenConnection open,
        Core.Connections.ConnectionInfo info,
        ConnectionSearchServices search
    )
    {
        var page = search.Options.CurrentValue.Search;
        return new RemoteSearchAsk
        {
            Target = ConnectionIndexTarget.Of(info, open.Capabilities),
            Query = query.Q,
            Folder = path,
            After = string.IsNullOrEmpty(query.After) ? null : query.After,
            Limit = Math.Clamp(query.Limit ?? page.DefaultPageSize, 1, Math.Max(1, page.MaxPageSize)),
            Sort = Enum.TryParse<SortField>(query.Sort, ignoreCase: true, out var sort) ? sort : SortField.Path,
            Descending = query.Desc,
            Visible = open.Folders.Visible(),
            HiddenIds = open.Folders.HiddenIds(),
        };
    }

    /// <summary>The page with only the items the session sees, each checked again by its path and id.</summary>
    private static ConnectionSearchResults AnswerOf(OpenConnection open, RemoteSearchPage found)
    {
        var items = found.Results.Items;
        var seen = items.Where((item, i) => open.Folders.At(item.Path, found.ProviderIds[i]).Has(Rights.Browse));
        var connection = open.Connection;
        return new ConnectionSearchResults
        {
            ConnectionId = connection.Id,
            Name = connection.Name,
            Kind = connection.Kind,
            Results = found.Results with { Items = [.. seen] },
            Access = connection.Access,
            Capabilities = open.Capabilities,
            Coverage = found.Coverage,
        };
    }
}
