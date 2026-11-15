using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Otools.Net.Sfe.Client.Tests.Infrastructure;

namespace Otools.Net.Sfe.Client.Tests.Facade;

/// <summary>
/// <see cref="SfeClient"/> against the real server: signing in with a token, the server's information, libraries,
/// registration in a program's services, and the generated layer reached through it.
/// </summary>
[TestClass]
public sealed class ClientTests
{
    public TestContext TestContext { get; set; } = null!;

    private CancellationToken Token => TestContext.CancellationToken;

    [TestMethod]
    public async Task TheServerTellsItsVersionAndTheCallersRightsOnce()
    {
        using var server = await TokenServer.StartAsync(Token);
        using var sfe = server.CreateClient();
        using var config = JsonDocument.Parse(await server.Bearer.GetStringAsync($"{V1}/config", Token));

        var info = await sfe.Server.GetInfoAsync(Token);
        var again = await sfe.Server.GetInfoAsync(Token);

        Assert.AreSame(info, again);
        Assert.AreEqual(config.RootElement.GetProperty("version").GetString(), info.Version);
        Assert.IsNotNull(info.ParsedVersion);
        Assert.AreEqual(SfeServerMode.External, info.Mode);
        Assert.IsTrue(info.HasRight("files.upload"));
        Assert.IsTrue(info.Supports(SfeFunction.Connections));
    }

    [TestMethod]
    public async Task AFunctionTurnedOffOnTheServerIsRefusedBeforeAnyCall()
    {
        using var server = await TokenServer.StartAsync(Token, settings =>
            settings["Sfe:Connections:Enabled"] = "false"
        );
        using var sfe = server.CreateClient();

        var refused = await Assert.ThrowsExactlyAsync<SfeNotSupportedException>(() =>
            sfe.Server.RequireAsync(SfeFunction.Connections, Token)
        );

        Assert.AreEqual(SfeFunction.Connections, refused.Function);
        Assert.AreEqual((await sfe.Server.GetInfoAsync(Token)).Version, refused.ServerVersion);
    }

    [TestMethod]
    public async Task TheLibrariesAreThoseTheAccountReaches()
    {
        using var server = await TokenServer.StartAsync(Token);
        using var sfe = server.CreateClient();

        var libraries = await sfe.Libraries.ListAsync(Token);
        var current = await sfe.Libraries.GetCurrentAsync(Token);

        Assert.HasCount(1, libraries);
        Assert.AreEqual(libraries[0].Id, current?.Id);
        Assert.IsFalse(string.IsNullOrEmpty(current?.Name));
    }

    [TestMethod]
    public async Task ALibraryChosenInTheOptionsIsSentWithEachCall()
    {
        using var server = await TokenServer.StartAsync(Token);
        using var probe = server.CreateClient();
        var library = (await probe.Libraries.GetCurrentAsync(Token))!;
        using var chosen = server.CreateClient(options => options.Library = library.Id);
        using var unknown = server.CreateClient(options => options.Library = library.Id + 1000);

        var listed = await chosen.Files.ListAsync("ChildDir", Token).ToListAsync(Token);
        var refused = await Assert.ThrowsExactlyAsync<SfeRefusedException>(() =>
            unknown.Files.ListAsync("ChildDir", Token).ToListAsync(Token).AsTask()
        );

        Assert.HasCount(2, listed);
        Assert.AreEqual(403, refused.StatusCode);
    }

    [TestMethod]
    public async Task AWrongTokenIsRefusedWith401()
    {
        using var server = await TokenServer.StartAsync(Token);
        using var sfe = server.CreateClient(options => options.Token = "sfe_wrong_token");

        var refused = await Assert.ThrowsExactlyAsync<SfeRefusedException>(() => sfe.Server.GetInfoAsync(Token));

        Assert.AreEqual(401, refused.StatusCode);
    }

    [TestMethod]
    public async Task AddSfeClientGivesAClientOfTheHttpClientFactory()
    {
        using var server = await TokenServer.StartAsync(Token);
        var services = new ServiceCollection();
        services
            .AddSfeClient(options =>
            {
                options.BaseAddress = server.Factory.Server.BaseAddress;
                options.Token = server.Token;
            })
            .ConfigurePrimaryHttpMessageHandler(() => server.Factory.Server.CreateHandler());
        using var provider = services.BuildServiceProvider();

        using var sfe = provider.GetRequiredService<SfeClient>();
        var listed = await sfe.Files.ListAsync("ChildDir", Token).ToListAsync(Token);

        Assert.HasCount(2, listed);
        Assert.AreNotSame(sfe, provider.GetRequiredService<SfeClient>());
    }

    [TestMethod]
    public async Task TheGeneratedClientIsReachedThroughTheFacadeWithTheSameToken()
    {
        using var server = await TokenServer.StartAsync(Token);
        using var sfe = server.CreateClient();

        var listing = await sfe.Raw.Api.V1.Browse.GetAsync(request => request.QueryParameters.Path = "ChildDir", Token);

        Assert.AreEqual("ChildDir", listing?.Path);
    }

    [TestMethod]
    public async Task AClientMadeWithAnAddressAndATokenCallsThatAddress()
    {
        // Its own HttpClient goes to the network, where the test server isn't: port 9 (discard) has no listener.
        using var made = new SfeClient(new Uri("http://127.0.0.1:9"), "sfe_test");

        await Assert.ThrowsAsync<HttpRequestException>(() => made.Server.GetInfoAsync(Token));
    }
}
