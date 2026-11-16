using Otools.Net.Sfe.Core.Access;
using Otools.Net.Sfe.Core.Connections;
using Otools.Net.Sfe.Core.Connections.Indexing;
using Otools.Net.Sfe.Core.Library;

namespace Otools.Net.Sfe.Api.Endpoints;

/// <summary>
/// A page of a connection's folder for <c>GET api/connections/{id}/browse</c>: from the library's index when it holds
/// the folder whole (<see cref="RemoteIndexListings"/>: no provider call), else from the provider
/// (<see cref="RemoteListings"/>, in a turn of its gate) with the items the index doesn't hold yet marked
/// (<c>indexed</c> false, as the library's listing marks files not scanned). Either way folder rules filter the
/// children before the page is cut, and each item shown is checked again by its path and id.
/// </summary>
internal static class ConnectionBrowsing
{
    public static async Task<IResult> PageAsync(
        ConnectionBrowseRequest request,
        ConnectionFileServices files,
        ConnectionBrowseServices browse,
        CancellationToken cancellationToken
    )
    {
        var open = request.Open;
        var target = await TargetAsync(files, open, cancellationToken);
        if (target is not null && await FromIndexAsync(request, target, browse, cancellationToken) is { } indexed)
            return await AnswerAsync(request, indexed, browse, (target, _ => true), cancellationToken);

        return await files.RunAsync(open, async storage =>
        {
            var read = await browse.Listings.ListAsync(storage, request.Asked, request.Filter, cancellationToken);
            var known = target is null
                ? null
                : await browse.IndexListings.IndexedAsync(target, read.Items, cancellationToken);
            var index = (target, known is null ? null : (Func<string, bool>)known.Contains);
            return await AnswerAsync(request, read, browse, index, cancellationToken);
        });
    }

    private static Task<RemoteListing?> FromIndexAsync(
        ConnectionBrowseRequest request,
        ConnectionIndexTarget target,
        ConnectionBrowseServices browse,
        CancellationToken cancellationToken
    )
    {
        var asked = request.Asked;
        var query = new IndexListingQuery(
            asked,
            target,
            request.Filter,
            request.Open.Folders.HiddenIds(),
            ProviderPaging: asked.Offset > 0 && browse.Listings.Holds(asked)
        );
        return browse.IndexListings.ListAsync(query, cancellationToken);
    }

    /// <summary>The connection as its index is made from it (root folder, account); null when it is gone.</summary>
    private static async Task<ConnectionIndexTarget?> TargetAsync(
        ConnectionFileServices files,
        OpenConnection open,
        CancellationToken cancellationToken
    ) =>
        await files.Connections.FindAsync(open.Connection.Id, cancellationToken) is { } info
            ? ConnectionIndexTarget.Of(info, open.Capabilities)
            : null;

    /// <param name="index">The connection as its index is made (null when gone), and whether the index holds the
    /// item at a path (null when nothing is told). The files' tags come from it where the folder lets the session
    /// see tags.</param>
    private static async Task<IResult> AnswerAsync(
        ConnectionBrowseRequest request,
        RemoteListing read,
        ConnectionBrowseServices browse,
        (ConnectionIndexTarget? Target, Func<string, bool>? Indexed) index,
        CancellationToken cancellationToken
    )
    {
        var (open, path) = (request.Open, request.Asked.Path);
        if (!IsSeenWhereListed(open, path, read))
            return TypedResults.NotFound();
        var listing = open.Folders.Seen(read);
        var page = ConnectionListings.Of(open, path, listing, request.Asked.Limit, browse.Classifier);
        var view = open.Folders.ViewOf(path, request.Here, listing.Items);
        var indexed = index.Indexed;
        IReadOnlyList<LibraryItem> items = indexed is null
            ? page.Listing.Items
            : [.. page.Listing.Items.Select(item => item with { Indexed = indexed(item.Path) })];
        if (index.Target is { } target && request.Here.Has(Rights.ViewTags))
            items = await browse.Tags.AttachAsync(RemoteTagPlace.Of(target), items, cancellationToken);
        // Sizes and lengths a thumbnail or the content cache told (agents/docs/features/connection-previews.md).
        if (index.Target is { } known)
            items = await browse.Media.AttachAsync(RemoteTagPlace.Of(known), items, cancellationToken);
        return TypedResults.Ok(page with { Listing = page.Listing with { Access = view, Items = items } });
    }

    /// <summary>
    /// The folder where the provider (or the index) has it (another case of its name, a name it folds) is one the
    /// session sees too: an empty page tells nothing.
    /// </summary>
    private static bool IsSeenWhereListed(OpenConnection open, string path, RemoteListing listing)
    {
        if (listing.Items.Count == 0 || path.Length == 0)
            return true;
        var listed = RemotePaths.Parent(listing.Items[0].Path);
        return listed == path || open.At(listed).IsVisible;
    }
}
