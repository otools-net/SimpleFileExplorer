using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Otools.Net.Sfe.Api.Endpoints;
using Otools.Net.Sfe.Api.Tenancy;
using Otools.Net.Sfe.Core.Access;
using Otools.Net.Sfe.Core.Connections;
using Otools.Net.Sfe.Core.Connections.Indexing;
using Otools.Net.Sfe.Core.Data;
using Otools.Net.Sfe.Tests.Infrastructure;

namespace Otools.Net.Sfe.Tests;

/// <summary>
/// <c>GET api/connections/{id}/search</c>: the library's index of a connection searched without calling the
/// provider; a first search starts the index; an account's own connection is searched by its owner only (never by
/// the library's administrators); folder rules leave hidden files out of the pages and the count, and a hidden
/// folder can't be searched.
/// </summary>
[TestClass]
public sealed class ConnectionSearchRouteTests
{
    private readonly FakeProvider _provider = new();
    private TestLibrary _library = null!;
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _admin = null!;
    private HttpClient _bob = null!;
    private int _tenant;
    private int _id;

    public TestContext TestContext { get; set; } = null!;

    private CancellationToken Token => TestContext.CancellationToken;

    [TestInitialize]
    public async Task InitializeAsync()
    {
        _library = new TestLibrary();
        _factory = ApiClients
            .CreateFactory(_library)
            .WithWebHostBuilder(builder =>
                builder.ConfigureServices(services => services.AddSingleton<IConnectionProvider>(_provider))
            );
        _admin = await ApiClients.SetUpAsync(_factory, Token);
        var accounts = _factory.Services.GetRequiredService<AccessService>();
        _tenant = (await accounts.GetTenantsAsync(Token)).Single().Id;
        var adminId = (await accounts.GetServerAdminIdsAsync(Token)).Single();
        // Bob administers the library, but an account's own connection is never his.
        var administrators = (await accounts.GetBuiltInGroupAsync(_tenant, BuiltInGroup.Administrators, Token))!.Id;
        await accounts.CreateUserAsync(new("bob", Password: "bob-password-1", GroupIds: [administrators]), null, Token);
        _bob = await ApiClients.SignInAsync(_factory, "bob", "bob-password-1", Token);

        _provider
            .Account("acct")
            .WithFile("Photos/Trips/Beach.jpg", "beach")
            .WithFile("Photos/beach towel.txt", "a")
            .WithFile("Readme.txt", "read me");
        var draft = new ConnectionDraft(FakeProvider.FakeKind, "Mine", ConnectionLevel.Account, OwnerId: adminId);
        var grant = new ConnectionGrant("acct", "acct@fake", "id:acct");
        _id = (
            await _factory.Services.GetRequiredService<ConnectionService>().CreateAsync(draft, grant, adminId, Token)
        )
            .Value!
            .Id;
    }

    [TestCleanup]
    public async Task CleanupAsync()
    {
        _bob.Dispose();
        _admin.Dispose();
        await _factory.DisposeAsync();
        SqliteConnection.ClearAllPools();
        _library.Dispose();
    }

    [TestMethod]
    public async Task AFirstSearchStartsTheIndexThenFindsWithoutCallingTheProvider()
    {
        // Answered at once from whatever the index holds then (its sync, which this starts, may be under way).
        var first = await SearchAsync(_admin, $"{V1}/connections/{_id}/search?q=beach");
        Assert.AreEqual("Mine", first.Name);

        await SyncedAsync();
        _provider.Account("acct").Calls.Clear();
        var found = await SearchAsync(_admin, $"{V1}/connections/{_id}/search?q=beach&sort=name");
        var photos = await SearchAsync(_admin, $"{V1}/connections/{_id}/search?q=beach&path=Photos/Trips");

        Assert.AreEqual(RemoteSearchCoverage.Complete, found.Coverage);
        Assert.AreEqual(("Mine", 2), (found.Name, found.Results.Total));
        CollectionAssert.AreEquivalent(
            new[] { "Photos/Trips/Beach.jpg", "Photos/beach towel.txt" },
            found.Results.Items.Select(i => i.Path).ToList()
        );
        CollectionAssert.AreEqual(new[] { "Beach.jpg" }, photos.Results.Items.Select(i => i.Name).ToList());
        Assert.IsEmpty(_provider.Account("acct").Calls, "the index answers alone");
    }

    [TestMethod]
    public async Task AnAccountsOwnConnectionIsSearchedByItsOwnerOnly()
    {
        await SearchAsync(_admin, $"{V1}/connections/{_id}/search?q=beach");
        await SyncedAsync();

        using var bob = await _bob.GetAsync($"{V1}/connections/{_id}/search?q=beach", Token);
        using var missing = await _admin.GetAsync($"{V1}/connections/{_id + 100}/search?q=beach", Token);
        using var nowhere = await _admin.GetAsync($"{V1}/connections/{_id}/search?q=beach&path=Nowhere/../..", Token);

        Assert.AreEqual(HttpStatusCode.NotFound, bob.StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, nowhere.StatusCode);
    }

    [TestMethod]
    public async Task FolderRulesLeaveHiddenFilesOutOfPagesAndCounts()
    {
        await using var world = await RuledConnection.CreateAsync(Token);

        var all = await SearchAsync(world.Bob, $"{world.Base}/search?q=");
        var secret = await SearchAsync(world.Bob, $"{world.Base}/search?q=secret, deep, h, l");
        var open = await SearchAsync(world.Bob, $"{world.Base}/search?q=&path=Lead/Open");
        var paged = await SearchAsync(world.Bob, $"{world.Base}/search?q=&limit=2&sort=size");
        using var hidden = await world.Bob.GetAsync($"{world.Base}/search?q=&path=Private", Token);
        using var mixedHidden = await world.Bob.GetAsync($"{world.Base}/search?q=&path=mixed/HIDDEN", Token);

        CollectionAssert.AreEquivalent(
            new[] { "Public/a.txt", "Public/b.jpg", "Mixed/ok.txt", "ReadOnly/r.txt", "Lead/Open/o.txt" },
            all.Results.Items.Select(i => i.Path).ToList()
        );
        Assert.AreEqual(5, all.Results.Total);
        Assert.IsEmpty(secret.Results.Items, "l.txt is in a folder that only leads to Open");
        Assert.AreEqual(0, secret.Results.Total);
        CollectionAssert.AreEqual(new[] { "o.txt" }, open.Results.Items.Select(i => i.Name).ToList());
        Assert.AreEqual(5, paged.Results.Total);
        Assert.AreEqual(HttpStatusCode.NotFound, hidden.StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, mixedHidden.StatusCode);
        Assert.IsEmpty(world.Account.Calls, "never asked of the provider");
    }

    private async Task<ConnectionSearchResults> SearchAsync(HttpClient client, string url)
    {
        using var answer = await client.GetAsync(url, Token);
        Assert.AreEqual(HttpStatusCode.OK, answer.StatusCode, await answer.Content.ReadAsStringAsync(Token));
        return (await answer.Content.ReadFromJsonAsync<ConnectionSearchResults>(RuledConnection.Json, Token))!;
    }

    /// <summary>The index synced once (10 s at most).</summary>
    private async Task SyncedAsync()
    {
        var host = await _factory.Services.GetRequiredService<TenantRegistry>().GetAsync(_tenant, Token);
        var indexes = host!.Services.GetRequiredService<ConnectionIndexes>();
        var until = DateTime.UtcNow.AddSeconds(10);
        while ((await indexes.StatusAsync(_id, Token)) is { Running: true } or { LastSync: null })
        {
            if (DateTime.UtcNow > until)
                throw new AssertFailedException("The connection's index was not synced.");
            await Task.Delay(20, Token);
        }
    }
}
