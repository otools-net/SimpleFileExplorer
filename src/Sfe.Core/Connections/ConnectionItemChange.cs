namespace Otools.Net.Sfe.Core.Connections;

/// <summary>Something SFE changed in a connection's files (<see cref="ConnectionChanges"/>).</summary>
/// <param name="Path">Where it was (moved, removed) or is (put), from the connection's root.</param>
/// <param name="Item">The item as the provider answered it (put, moved); null when removed.</param>
public sealed record ConnectionItemChange(
    int ConnectionId,
    ProviderCapabilities Capabilities,
    ConnectionItemChangeKind Kind,
    string Path,
    RemoteItem? Item
)
{
    public static ConnectionItemChange Put(int id, ProviderCapabilities capabilities, RemoteItem item) =>
        new(id, capabilities, ConnectionItemChangeKind.Put, item.Path, item);

    public static ConnectionItemChange Copied(int id, ProviderCapabilities capabilities, RemoteItem item) =>
        new(id, capabilities, ConnectionItemChangeKind.Copied, item.Path, item);

    public static ConnectionItemChange Moved(int id, ProviderCapabilities capabilities, string from, RemoteItem item) =>
        new(id, capabilities, ConnectionItemChangeKind.Moved, from, item);

    public static ConnectionItemChange Removed(int id, ProviderCapabilities capabilities, string path) =>
        new(id, capabilities, ConnectionItemChangeKind.Removed, path, null);

    /// <summary>The folders whose listings changed: where it was, and where it is.</summary>
    public IEnumerable<string> Folders() =>
        Item is { } item && Kind == ConnectionItemChangeKind.Moved
            ? new[] { RemotePaths.Parent(Path), RemotePaths.Parent(item.Path) }.Distinct(StringComparer.Ordinal)
            : [RemotePaths.Parent(Path)];
}
