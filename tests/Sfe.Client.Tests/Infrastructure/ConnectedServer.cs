using Otools.Net.Sfe.Core.Connections;
using Otools.Net.Sfe.Tests.Infrastructure;

namespace Otools.Net.Sfe.Client.Tests.Infrastructure;

/// <summary>
/// A <see cref="TokenServer"/> knowing the <see cref="FakeProvider"/> of the server's tests, with one connection to
/// its account <c>acct</c> (the administrator's own, or the library's) and the library's client.
/// </summary>
public sealed class ConnectedServer : IDisposable
{
    private ConnectedServer(TokenServer server, FakeProvider provider, int id)
    {
        Server = server;
        Provider = provider;
        Id = id;
        Client = server.CreateClient();
    }

    /// <summary>The server, with the library and the administrator's token.</summary>
    public TokenServer Server { get; }

    /// <summary>The provider: its capabilities, and what its accounts answer.</summary>
    public FakeProvider Provider { get; }

    /// <summary>The connection's account at the provider: its files, the calls that reached it.</summary>
    public FakeAccount Account => Provider.Account("acct");

    /// <summary>The connection's id.</summary>
    public int Id { get; }

    /// <summary>The library's client of the server, with the token.</summary>
    public SfeClient Client { get; }

    /// <summary>The connection, through the client.</summary>
    public SfeConnection Connection => Client.Connections[Id];

    /// <summary>
    /// Starts the server with a provider whose capabilities <paramref name="capabilities"/> may change, and connects
    /// to its account <c>acct</c> holding <c>Photos/a.mp4</c> (10 bytes), <c>Photos/b.jpg</c> and an empty
    /// <c>Photos/Trips</c>.
    /// </summary>
    public static async Task<ConnectedServer> StartAsync(
        CancellationToken cancellationToken,
        ConnectionLevel level = ConnectionLevel.Account,
        Func<ProviderCapabilities, ProviderCapabilities>? capabilities = null
    )
    {
        var provider = new FakeProvider();
        if (capabilities is not null)
            provider.Capabilities = capabilities(provider.Capabilities);
        provider
            .Account("acct")
            .WithFolder("Photos/Trips")
            .WithFile("Photos/a.mp4", "0123456789")
            .WithFile("Photos/b.jpg", "picture b");
        var server = await TokenServer.StartAsync(cancellationToken, provider: provider);
        var id = await server.ConnectAsync("acct", level, cancellationToken);
        return new ConnectedServer(server, provider, id);
    }

    public void Dispose()
    {
        Client.Dispose();
        Server.Dispose();
    }
}
