using Otools.Net.Sfe.Core.Connections;
using Otools.Net.Sfe.Core.Library;

namespace Otools.Net.Sfe.Api.Endpoints;

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
        // The provider refusing changes to it (a folder shared for viewing only): its lock in listings.
        bool? readOnly = item.ReadOnly ? true : null;
        if (item.IsFolder)
            return new LibraryItem(item.Name, item.Path, ItemKind.Directory, null, null, modified) with
            {
                ReadOnly = readOnly,
            };

        var extension = RemoteSort.ExtensionOf(item);
        var kind = classifier.Classify(extension);
        return new LibraryItem(item.Name, item.Path, kind, extension, item.Size, modified) with { ReadOnly = readOnly };
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
