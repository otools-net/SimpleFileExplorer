using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Otools.Net.Sfe.Client.Connections;
using Otools.Net.Sfe.Client.Http;

namespace Otools.Net.Sfe.Client;

/// <summary>
/// Connections: storage outside the libraries (Dropbox…) the caller may use in its library, each a place of its own
/// with its files (<c>sfe.Connections[id].Files</c>) and transfers with the library. Made and managed on the web
/// page (their sign-in goes through a browser); their administration is reached through <see cref="SfeClient.Raw"/>.
/// </summary>
/// <example>
/// <code>
/// var dropbox = (await sfe.Connections.ListAsync()).First(c => c.Kind == "dropbox");
/// await foreach (var item in sfe.Connections[dropbox.Id].Files.ListAsync(""))
///     Console.WriteLine(item.Name);
/// </code>
/// </example>
public sealed class SfeConnections
{
    private readonly SfeTransport _transport;
    private readonly SfeServer _server;
    private readonly ConcurrentDictionary<int, SfeConnection> _connections = new();

    internal SfeConnections(SfeTransport transport, SfeServer server)
    {
        _transport = transport;
        _server = server;
    }

    /// <summary>
    /// The connection <paramref name="id"/>, to call: nothing is asked of the server until a call is made (one the
    /// caller may not use answers <see cref="SfeNotFoundException"/>).
    /// </summary>
    /// <param name="id">The connection's id (<see cref="SfeConnectionInfo.Id"/>).</param>
    public SfeConnection this[int id] => _connections.GetOrAdd(id, key => new SfeConnection(_transport, this, key));

    /// <summary>The connections the caller may use in its library, and how far, as the server tells it now.</summary>
    /// <param name="cancellationToken">Stops waiting for the server.</param>
    /// <exception cref="SfeNotSupportedException">The server has connections off, or none for this account.</exception>
    public async Task<IReadOnlyList<SfeConnectionInfo>> ListAsync(CancellationToken cancellationToken = default)
    {
        await _server.RequireAsync(SfeFunction.Connections, cancellationToken).ConfigureAwait(false);
        var info = await _server.GetInfoAsync(cancellationToken).ConfigureAwait(false);
        var answer = await _transport
            .ReadAsync<ConnectionsAnswer>(SfeRequest.Get(Routes.V1("connections")), cancellationToken)
            .ConfigureAwait(false);
        return [.. answer.Usable.Select(connection => InfoOf(connection, info.ConnectionKinds))];
    }

    private static SfeConnectionInfo InfoOf(
        UsableConnection connection,
        IReadOnlyDictionary<string, SfeConnectionCapabilities> kinds
    ) =>
        new()
        {
            Id = connection.Id,
            Kind = connection.Kind,
            Name = connection.Name,
            Level = connection.Level,
            State = connection.State,
            Access = connection.Access,
            Capabilities = kinds.TryGetValue(connection.Kind, out var capabilities) ? capabilities : null,
        };
}
