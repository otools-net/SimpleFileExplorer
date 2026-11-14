using Microsoft.AspNetCore.Mvc;
using Sfe.Api.Security;
using Sfe.Core.Access;
using Sfe.Core.Connections;
using Sfe.Core.Library;

namespace Sfe.Api.Endpoints;

/// <summary>
/// A connection's files, browsed and read: the library's routes with the connection's id in front. Paths are
/// relative to the connection's root folder and go through <see cref="RemotePaths.Normalize"/> before a provider
/// sees them. A connection the session may not use answers 404 (<see cref="ConnectionAccess"/>); the route's
/// <c>files.*</c> right is asked as in the library, and at the path by the connection's folder rules
/// (<see cref="OpenConnection.Check(string, string)"/>) before the provider is called: 404 for what the session
/// doesn't see, 403 for what it sees but may not do. A single item is checked again where the provider says it is.
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

    private const string PropertiesDescription = """
        The item as a listing shows it (`item`; the root folder named after the connection), the provider's kind,
        the content hash the provider gives (`contentHash`, of the kind `hashName` names) and whether the file has
        content to download. `account` (the provider's account) is given only on the caller's own connection.
        """;

    public static RouteGroupBuilder MapConnectionFileEndpoints(this RouteGroupBuilder api)
    {
        var connections = api.MapGroup("/connections/{id:int}").WithTags("Connections");

        connections
            .MapGet("/browse", Browse)
            .RequireRateLimiting(RequestLimits.Connections)
            .RequireRight(Rights.UseConnections)
            .FolderScoped("library.browse; 404 for a connection the subject may not use")
            .WithName("BrowseConnection")
            .WithSummary("Lists a folder of a connection (folders first).")
            .WithDescription(BrowseDescription);

        connections
            .MapGet("/files/raw/{**path}", Raw)
            .RequireRateLimiting(RequestLimits.Connections)
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

        connections
            .MapGet("/files/properties", Properties)
            .RequireRateLimiting(RequestLimits.Connections)
            .RequireRight(Rights.UseConnections)
            .FolderScoped("library.browse; 404 for a connection the subject may not use")
            .WithName("GetConnectionItemProperties")
            .WithSummary("The properties of a file or folder of a connection (`path` empty: its root folder).")
            .WithDescription(PropertiesDescription);

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
        // A folder only leading to what the session sees is listed with only those children.
        if (open.At(path) is not { IsVisible: true } here)
            return TypedResults.NotFound();

        var sort = Enum.TryParse<SortField>(query.Sort, ignoreCase: true, out var parsed) ? parsed : SortField.Name;
        var page = browse.Options.CurrentValue.Browse;
        var limit = Math.Clamp(query.Limit ?? page.DefaultPageSize, 1, Math.Max(1, page.MaxPageSize));
        var asked = new RemoteListingQuery(id, path, Math.Max(0, query.Offset), limit, sort, query.Desc);
        var filter = open.Folders.ListingFilterOf(path, here);
        return await files.RunAsync(open, async storage =>
        {
            var listing = await browse.Listings.ListAsync(storage, asked, filter, cancellationToken);
            if (!IsSeenWhereListed(open, path, listing))
                return TypedResults.NotFound();
            var page = ConnectionListings.Of(open, path, listing, limit, browse.Classifier);
            var view = open.Folders.ViewOf(path, here);
            return TypedResults.Ok(page with { Listing = page.Listing with { Access = view } });
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
        var right = download ? Rights.Download : Rights.ViewFiles;
        if (Refusal(open, normalized, right) is { } refused)
            return refused;

        return await files.RunAsync(open, async storage =>
        {
            if (await storage.GetAsync(normalized, cancellationToken) is not { IsFolder: false } item)
                return TypedResults.NotFound();
            if (OpenConnection.AnswerOf(Stricter(open, normalized, item, right)) is { } there)
                return there;
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
        if (OpenConnection.AnswerOf(open.Check(normalized, Rights.ViewFiles)) is { } refused)
            return refused;

        return await files.RunAsync(open, async storage =>
        {
            // Where the provider says the file is, for a session folder rules restrict (one more call).
            if (
                !open.Folders.IsUnrestricted
                && await SeenThereAsync(open, storage, normalized, cancellationToken) is { } there
            )
            {
                return there;
            }

            var thumbnail = await storage.ThumbnailAsync(normalized, Math.Clamp(size, 32, 1024), cancellationToken);
            if (thumbnail is null)
                return TypedResults.NotFound();
            files.Context.Response.Headers.CacheControl = "no-store";
            return TypedResults.Stream(thumbnail.Stream, thumbnail.ContentType ?? "image/jpeg");
        });
    }

    private static async Task<IResult> Properties(
        int id,
        [AsParameters] ConnectionFileServices files,
        MediaClassifier classifier,
        [FromQuery(Name = "path")] string? path = null,
        CancellationToken cancellationToken = default
    )
    {
        if (RemotePaths.Normalize(path) is not { } normalized)
            return TypedResults.NotFound();
        var (open, answer) = await files.OpenAsync(id, Rights.Browse, cancellationToken);
        if (open is null)
            return answer!;
        if (OpenConnection.AnswerOf(open.Check(normalized, Rights.Browse)) is { } refused)
            return refused;
        var account = await OwnAccountAsync(files, open.Connection, cancellationToken);

        return await files.RunAsync(open, async storage =>
        {
            if (await storage.GetAsync(normalized, cancellationToken) is not { } item)
                return TypedResults.NotFound();
            if (OpenConnection.AnswerOf(open.Check(normalized, item, Rights.Browse)) is { } there)
                return there;
            if (normalized.Length == 0)
                item = item with { Name = open.Connection.Name };
            return TypedResults.Ok(
                new ConnectionItemProperties(
                    ConnectionListings.ToItem(item, classifier),
                    open.Connection.Kind,
                    account,
                    item.Hash,
                    open.Capabilities.ContentHash,
                    item.Downloadable
                )
            );
        });
    }

    /// <summary>
    /// A raw read refused at the path asked: 404 unseen, 403 seen but not viewable (or downloadable when it is a
    /// download); null when allowed.
    /// </summary>
    private static IResult? Refusal(OpenConnection open, string path, string right) =>
        OpenConnection.AnswerOf(Stricter(open.Check(path, Rights.ViewFiles), open.Check(path, right)));

    private static PathVerdict Stricter(OpenConnection open, string asked, RemoteItem item, string right) =>
        Stricter(open.Check(asked, item, Rights.ViewFiles), open.Check(asked, item, right));

    private static PathVerdict Stricter(PathVerdict one, PathVerdict other) => one > other ? one : other;

    /// <summary>
    /// The file where the provider says it is: not found when it isn't there or the session doesn't see it there,
    /// 403 when it may not view it; null when it may.
    /// </summary>
    private static async Task<IResult?> SeenThereAsync(
        OpenConnection open,
        IRemoteStorage storage,
        string path,
        CancellationToken cancellationToken
    )
    {
        if (await storage.GetAsync(path, cancellationToken) is not { IsFolder: false } item)
            return TypedResults.NotFound();
        return OpenConnection.AnswerOf(open.Check(path, item, Rights.ViewFiles));
    }

    /// <summary>
    /// The folder where the provider listed it (another case of its name, a name it folds) is one the session sees
    /// too: an empty page tells nothing.
    /// </summary>
    private static bool IsSeenWhereListed(OpenConnection open, string path, RemoteListing listing)
    {
        if (listing.Items.Count == 0 || path.Length == 0)
            return true;
        var listed = RemotePaths.Parent(listing.Items[0].Path);
        return listed == path || open.At(listed).IsVisible;
    }

    /// <summary>The provider's account of an account-level connection the caller owns; null for any other.</summary>
    private static async Task<string?> OwnAccountAsync(
        ConnectionFileServices files,
        UsableConnection connection,
        CancellationToken cancellationToken
    )
    {
        if (connection.Level != ConnectionLevel.Account || (await files.Current.GetAsync()).User is not { } user)
            return null;
        var info = await files.Connections.FindAsync(connection.Id, cancellationToken);
        return info is { Level: ConnectionLevel.Account } && info.OwnerId == user.Id ? info.Account : null;
    }
}
