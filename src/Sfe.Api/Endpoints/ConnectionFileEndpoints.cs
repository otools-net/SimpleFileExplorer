using Sfe.Api.Security;
using Sfe.Core.Access;
using Sfe.Core.Connections;
using Sfe.Core.Library;

namespace Sfe.Api.Endpoints;

/// <summary>
/// A connection's files, browsed and read: the library's routes with the connection's id in front. Paths are
/// relative to the connection's root folder and go through <see cref="RemotePaths.Normalize"/> before a provider
/// sees them. A connection the session may not use answers 404 (<see cref="ConnectionAccess"/>); the route's
/// <c>files.*</c> right is asked as in the library.
/// </summary>
public static class ConnectionFileEndpoints
{
    private const string BrowseDescription = """
        The listing of `GET api/browse` (`listing`), with how far the session uses the connection (`access`) and
        what its provider can do (`capabilities`). The first page (`offset` 0) reads the folder from the provider;
        the next ones reuse that read for a few minutes. A folder too large to read whole comes in the provider's
        order (`sorted` false) and `hasMore` tells that the next pages read on. A connection whose sign-in the
        provider no longer accepts answers 409 `connection-needs-reconnecting`; the provider asking to wait, 429
        `connection-rate-limited` with `Retry-After`; the provider unreachable, 502 `connection-unavailable`.
        """;

    public static RouteGroupBuilder MapConnectionFileEndpoints(this RouteGroupBuilder api)
    {
        var connections = api.MapGroup("/connections/{id:int}").WithTags("Connections");

        connections
            .MapGet("/browse", Browse)
            .RequireRight(Rights.UseConnections)
            .FolderScoped("library.browse; 404 for a connection the subject may not use")
            .WithName("BrowseConnection")
            .WithSummary("Lists a folder of a connection (folders first).")
            .WithDescription(BrowseDescription);

        connections
            .MapGet("/files/raw/{**path}", Raw)
            .RequireRight(Rights.UseConnections)
            .NeverRunByPages()
            .FolderScoped("files.view (files.download to download); 404 for a connection the subject may not use")
            .WithName("GetConnectionFile")
            .WithSummary("Streams a file of a connection (one HTTP range when the provider reads parts).")
            .WithDescription("`download=true` asks the browser to save it, which needs the right `files.download`.");

        connections
            .MapGet("/thumbnails/{**path}", Thumbnail)
            .RequireRateLimiting(RequestLimits.Media)
            .RequireRight(Rights.UseConnections)
            .FolderScoped("files.view; 404 for a connection the subject may not use")
            .WithName("GetConnectionThumbnail")
            .WithSummary("JPEG preview of a picture or video of a connection, made by its provider.");

        return api;
    }

    private static async Task<IResult> Browse(
        int id,
        [AsParameters] ConnectionFileServices files,
        [AsParameters] ConnectionBrowseServices browse,
        [AsParameters] ConnectionBrowseQuery query,
        CancellationToken cancellationToken
    )
    {
        if (RemotePaths.Normalize(query.Path) is not { } path)
            return TypedResults.NotFound();
        var (open, answer) = await files.OpenAsync(id, Rights.Browse, cancellationToken);
        if (open is null)
            return answer!;

        var sort = Enum.TryParse<SortField>(query.Sort, ignoreCase: true, out var parsed) ? parsed : SortField.Name;
        var page = browse.Options.CurrentValue.Browse;
        var limit = Math.Clamp(query.Limit ?? page.DefaultPageSize, 1, Math.Max(1, page.MaxPageSize));
        var asked = new RemoteListingQuery(id, path, Math.Max(0, query.Offset), limit, sort, query.Desc);
        return await files.RunAsync(open, async storage =>
        {
            var listing = await browse.Listings.ListAsync(storage, asked, cancellationToken);
            return TypedResults.Ok(ConnectionListings.Of(open, path, listing, limit, browse.Classifier));
        });
    }

    private static async Task<IResult> Raw(
        int id,
        string path,
        [AsParameters] ConnectionFileServices files,
        bool download = false,
        CancellationToken cancellationToken = default
    )
    {
        if (RemotePaths.Normalize(path) is not { Length: > 0 } normalized)
            return TypedResults.NotFound();
        var (open, answer) = await files.OpenAsync(id, Rights.ViewFiles, cancellationToken);
        if (open is null)
            return answer!;
        if (download && !(await files.Current.GetAsync()).Has(Rights.Download))
            return TypedResults.Forbid();

        return await files.RunAsync(open, async storage =>
        {
            if (await storage.GetAsync(normalized, cancellationToken) is not { IsFolder: false } item)
                return TypedResults.NotFound();
            RemoteRange? range = null;
            if (open.Capabilities.RangedReads)
            {
                range = RemoteRanges.Parse(files.Context.Request.Headers.Range, item.Size, out var past);
                if (past)
                {
                    files.Context.Response.Headers.ContentRange = $"bytes */{item.Size}";
                    return TypedResults.StatusCode(StatusCodes.Status416RangeNotSatisfiable);
                }
            }
            var content = await storage.OpenReadAsync(normalized, range, cancellationToken);
            return new RemoteFileResult(content, item, download, open.Capabilities.RangedReads);
        });
    }

    private static async Task<IResult> Thumbnail(
        int id,
        string path,
        [AsParameters] ConnectionFileServices files,
        int size = 256,
        CancellationToken cancellationToken = default
    )
    {
        if (RemotePaths.Normalize(path) is not { Length: > 0 } normalized)
            return TypedResults.NotFound();
        var (open, answer) = await files.OpenAsync(id, Rights.ViewFiles, cancellationToken);
        if (open is null)
            return answer!;
        if (!open.Capabilities.Thumbnails)
            return TypedResults.NotFound();

        return await files.RunAsync(open, async storage =>
        {
            var thumbnail = await storage.ThumbnailAsync(normalized, Math.Clamp(size, 32, 1024), cancellationToken);
            if (thumbnail is null)
                return TypedResults.NotFound();
            files.Context.Response.Headers.CacheControl = "no-store";
            return TypedResults.Stream(thumbnail.Stream, thumbnail.ContentType ?? "image/jpeg");
        });
    }
}
