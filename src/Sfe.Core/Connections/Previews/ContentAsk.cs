namespace Otools.Net.Sfe.Core.Connections.Previews;

/// <summary>
/// A file of a connection wanted whole in the cache (<see cref="ConnectionContentCache.OpenAsync"/>).
/// </summary>
/// <param name="Item">The file as the provider told it just now: its id and version make the key.</param>
/// <param name="CaseInsensitive">The provider ignores the case of names (a file without an id).</param>
/// <param name="Caller">Who asks (an account or a network address): each one downloads only a few at once.</param>
public sealed record ContentAsk(int ConnectionId, RemoteItem Item, bool CaseInsensitive, string Caller);
