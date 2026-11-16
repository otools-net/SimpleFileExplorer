using Otools.Net.Sfe.Core.Connections.Indexing;
using Otools.Net.Sfe.Core.Library;

namespace Otools.Net.Sfe.Core.Connections.Previews;

/// <summary>A file of a connection to preview (<see cref="ConnectionPreviews"/>), its access already checked.</summary>
/// <param name="Item">The file as the provider (or the library's index of it) tells it: its id and version make the
/// cache's key.</param>
/// <param name="Kind">What the library calls it by its extension (picture, video, audio).</param>
/// <param name="Place">The library's rows of the connection, which get the file's size and length; null when the
/// library has no index of it.</param>
/// <param name="Caller">Who asks (an account or a network address), for the cache's limits per caller.</param>
public sealed record PreviewAsk(
    int ConnectionId,
    RemoteItem Item,
    ItemKind Kind,
    ProviderCapabilities Capabilities,
    RemoteTagPlace? Place,
    string Caller
)
{
    public RemoteCacheKey Key => RemoteCacheKey.Of(Item, Capabilities.CaseInsensitiveNames);

    public ContentAsk Content => new(ConnectionId, Item, Capabilities.CaseInsensitiveNames, Caller);
}
