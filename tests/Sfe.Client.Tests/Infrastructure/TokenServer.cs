using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Kiota.Abstractions.Authentication;
using Microsoft.Kiota.Http.HttpClientLibrary;
using Otools.Net.Sfe.Api.Endpoints;
using Otools.Net.Sfe.Client.Generated;
using Otools.Net.Sfe.Core.Access;
using Otools.Net.Sfe.Tests.Infrastructure;

namespace Otools.Net.Sfe.Client.Tests.Infrastructure;

/// <summary>
/// A test server in process on a fresh <see cref="TestLibrary"/>, set up with its administrator, and a program calling
/// it with a personal access token of that administrator, as a third-party app would.
/// </summary>
public sealed class TokenServer : IDisposable
{
    private readonly HttpClient _bearer;

    private TokenServer(TestLibrary library, WebApplicationFactory<Program> factory, HttpClient bearer)
    {
        Library = library;
        Factory = factory;
        _bearer = bearer;
    }

    /// <summary>The library the server shows (its files are under <see cref="TestLibrary.Root"/>).</summary>
    public TestLibrary Library { get; }

    /// <summary>The server in process.</summary>
    public WebApplicationFactory<Program> Factory { get; }

    /// <summary>
    /// Starts a server (<c>Sfe:Mode=External</c>, as <see cref="TestLibrary.Settings"/> sets it: the test client has
    /// no remote address) and makes a token of its administrator.
    /// </summary>
    public static async Task<TokenServer> StartAsync(CancellationToken cancellationToken)
    {
        var library = new TestLibrary();
        var factory = ApiClients.CreateFactory(library);
        using var admin = await ApiClients.SetUpAsync(factory, cancellationToken);
        var session = await TokenClients.GetAsync<SessionInfo>(admin, $"{V1}/session", cancellationToken);
        var draft = new AccessTokenDraft("Client library tests");
        var token = await TokenClients.CreateAsync(factory, session.User!.Id, draft, cancellationToken);
        return new TokenServer(library, factory, TokenClients.Bearer(factory, token.Secret));
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

    public void Dispose()
    {
        _bearer.Dispose();
        Factory.Dispose();
        Library.Dispose();
    }
}
