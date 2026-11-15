using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Kiota.Abstractions.Authentication;
using Microsoft.Kiota.Http.HttpClientLibrary;
using Otools.Net.Sfe.Api.Endpoints;
using Otools.Net.Sfe.Client.Generated;
using Otools.Net.Sfe.Core.Access;
using Otools.Net.Sfe.Core.Indexing;
using Otools.Net.Sfe.Tests.Infrastructure;

namespace Otools.Net.Sfe.Client.Tests.Infrastructure;

/// <summary>
/// A test server in process on a fresh <see cref="TestLibrary"/>, set up with its administrator, and a program calling
/// it with a personal access token of that administrator, as a third-party app would.
/// </summary>
public sealed class TokenServer : IDisposable
{
    private readonly HttpClient _bearer;
    private readonly List<HttpClient> _clients = [];

    private TokenServer(TestLibrary library, WebApplicationFactory<Program> factory, HttpClient bearer, string token)
    {
        Library = library;
        Factory = factory;
        _bearer = bearer;
        Token = token;
    }

    /// <summary>The library the server shows (its files are under <see cref="TestLibrary.Root"/>).</summary>
    public TestLibrary Library { get; }

    /// <summary>The server in process.</summary>
    public WebApplicationFactory<Program> Factory { get; }

    /// <summary>The administrator's token (<c>sfe_…</c>).</summary>
    public string Token { get; }

    /// <summary>
    /// A client sending the token in its default headers, for checks made beside the library's calls.
    /// </summary>
    public HttpClient Bearer => _bearer;

    /// <summary>
    /// Starts a server (<c>Sfe:Mode=External</c>, as <see cref="TestLibrary.Settings"/> sets it: the test client has
    /// no remote address) and makes a token of its administrator.
    /// </summary>
    public static async Task<TokenServer> StartAsync(
        CancellationToken cancellationToken,
        Action<IDictionary<string, string?>>? configure = null
    )
    {
        var library = new TestLibrary();
        var factory = ApiClients.CreateFactory(library, configure);
        using var admin = await ApiClients.SetUpAsync(factory, cancellationToken);
        var session = await TokenClients.GetAsync<SessionInfo>(admin, $"{V1}/session", cancellationToken);
        var draft = new AccessTokenDraft("Client library tests");
        var token = await TokenClients.CreateAsync(factory, session.User!.Id, draft, cancellationToken);
        return new TokenServer(library, factory, TokenClients.Bearer(factory, token.Secret), token.Secret);
    }

    /// <summary>
    /// The library's client of this server with the token, on an <see cref="HttpClient"/> straight to the server
    /// (no default header: the client adds its own); <paramref name="wrap"/> puts a handler of the test's in front.
    /// </summary>
    public SfeClient CreateClient(
        Action<SfeClientOptions>? configure = null,
        Func<HttpMessageHandler, HttpMessageHandler>? wrap = null
    )
    {
        var handler = Factory.Server.CreateHandler();
        var http = new HttpClient(wrap?.Invoke(handler) ?? handler) { BaseAddress = Factory.Server.BaseAddress };
        lock (_clients)
            _clients.Add(http);
        var options = new SfeClientOptions { Token = Token, PollInterval = TimeSpan.FromMilliseconds(50) };
        configure?.Invoke(options);
        return new SfeClient(http, options);
    }

    /// <summary>
    /// The generated client over the server's <see cref="HttpClient"/>: the token is in its default headers, so Kiota
    /// adds no authentication of its own.
    /// </summary>
    public SfeRawClient CreateRawClient()
    {
        var adapter = new HttpClientRequestAdapter(new AnonymousAuthenticationProvider(), httpClient: _bearer)
        {
            // Kiota's routes start with "/": "http://localhost/" would give "//api/v1/…".
            BaseUrl = _bearer.BaseAddress!.ToString().TrimEnd('/'),
        };
        return new SfeRawClient(adapter);
    }

    /// <summary>
    /// Indexes the whole library (a scan from the root, as an administrator starts it) and waits until it ends: the
    /// search finds only indexed files.
    /// </summary>
    public async Task IndexAsync(CancellationToken cancellationToken)
    {
        var request = new ScanRequest("");
        using (var start = await _bearer.PostAsJsonAsync($"{V1}/scan", request, TokenClients.Json, cancellationToken))
            start.EnsureSuccessStatusCode();
        for (var attempt = 0; attempt < 400; attempt++)
        {
            var status = await TokenClients.GetAsync<ScanStatus>(_bearer, $"{V1}/scan", cancellationToken);
            if (status.State != ScanState.Running)
                return;
            await Task.Delay(50, cancellationToken);
        }
        throw new AssertFailedException("The scan did not finish in time.");
    }

    public void Dispose()
    {
        foreach (var client in _clients)
            client.Dispose();
        _bearer.Dispose();
        Factory.Dispose();
        Library.Dispose();
    }
}
