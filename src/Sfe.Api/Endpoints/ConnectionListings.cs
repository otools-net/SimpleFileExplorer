using Sfe.Core.Connections;
using Sfe.Core.Library;

namespace Sfe.Api.Endpoints;

/// <summary>A page of a connection's folder in the library's listing shape, so the client lists both alike.</summary>
internal static class ConnectionListings
{
    public static ConnectionListing Of(
        OpenConnection open,
        string path,
        RemoteListing listing,
        int limit,
        MediaClassifier classifier
    )
    {
        var connection = open.Connection;
        var items = listing.Items.Select(i => ToItem(i, classifier)).ToList();
        var directory = new DirectoryListing(
            Path: path,
            Name: path.Length == 0 ? connection.Name : RemotePaths.NameOf(path),
            ParentPath: path.Length == 0 ? null : RemotePaths.Parent(path),
            Breadcrumbs: Breadcrumbs(connection.Name, path),
            Items: items,
            Offset: listing.Offset,
            Limit: limit,
            Total: listing.Total,
            DirectoryCount: listing.DirectoryCount,
            FileCount: listing.Total - listing.DirectoryCount
        );
        return new ConnectionListing(
            connection.Id,
            connection.Name,
            connection.Kind,
            directory,
            connection.Access,
            listing.Sorted,
            listing.HasMore,
            open.Capabilities
        );
    }

    public static LibraryItem ToItem(RemoteItem item, MediaClassifier classifier)
    {
        DateTimeOffset? modified = item.Modified is { } date ? new DateTimeOffset(date, TimeSpan.Zero) : null;
        if (item.IsFolder)
            return new LibraryItem(item.Name, item.Path, ItemKind.Directory, null, null, modified);

        var extension = RemoteSort.ExtensionOf(item);
        return new LibraryItem(item.Name, item.Path, classifier.Classify(extension), extension, item.Size, modified);
    }

    private static IReadOnlyList<Breadcrumb> Breadcrumbs(string rootName, string path)
    {
        var crumbs = new List<Breadcrumb> { new(rootName, "") };
        var current = "";
        foreach (var part in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            current = RemotePaths.Combine(current, part);
            crumbs.Add(new Breadcrumb(part, current));
        }
        return crumbs;
    }
}
