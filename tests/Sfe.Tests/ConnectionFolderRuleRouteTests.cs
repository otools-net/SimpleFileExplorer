using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Sfe.Api.Endpoints;
using Sfe.Core.Access;
using Sfe.Core.Connections;
using Sfe.Core.Data;
using Sfe.Tests.Infrastructure;

namespace Sfe.Tests;

/// <summary>
/// The folder rules a library sets in a connection (<c>api/connections/{id}/folder-rules</c> and <c>folders</c>):
/// only for those who manage the library's connections, 404 for a connection the library doesn't reach, the
/// service's refusals as validation answers, and every folder in the picker.
/// </summary>
[TestClass]
public sealed class ConnectionFolderRuleRouteTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private readonly FakeProvider _provider = new();
    private TestLibrary _library = null!;
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _admin = null!;
    private HttpClient _alice = null!;
    private HttpClient _bob = null!;
    private int _tenant;
    private int _users;
    private int _bobId;
    private int _id;

    public TestContext TestContext { get; set; } = null!;

    private CancellationToken Cancel => TestContext.CancellationToken;

    [TestInitialize]
    public async Task InitializeAsync()
    {
        _library = new TestLibrary();
        _factory = ApiClients
            .CreateFactory(_library)
            .WithWebHostBuilder(builder =>
                builder.ConfigureServices(services => services.AddSingleton<IConnectionProvider>(_provider))
            );
        _admin = await ApiClients.SetUpAsync(_factory, Cancel);
        var accounts = _factory.Services.GetRequiredService<AccessService>();
        _tenant = (await accounts.GetTenantsAsync(Cancel)).Single().Id;
        _users = (await accounts.GetBuiltInGroupAsync(_tenant, BuiltInGroup.Users, Cancel))!.Id;
        var admins = (await accounts.GetBuiltInGroupAsync(_tenant, BuiltInGroup.Administrators, Cancel))!.Id;
        await accounts.CreateUserAsync(new("alice", Password: "alice-password", GroupIds: [admins]), null, Cancel);
        _bobId = (
            await accounts.CreateUserAsync(new("bob", Password: "bob-password-1", GroupIds: [_users]), null, Cancel)
        )
            .Value!
            .Id;
        _alice = await ApiClients.SignInAsync(_factory, "alice", "alice-password", Cancel);
        _bob = await ApiClients.SignInAsync(_factory, "bob", "bob-password-1", Cancel);

        _provider
            .Account("acct")
            .WithFolder("Photos/Trips")
            .WithFolder("Photos/Family")
            .WithFolder("Music")
            .WithFile("Photos/a.jpg", "picture")
            .WithFile("notes.txt", "text");
        _id = (await MakeAsync(new(FakeProvider.FakeKind, "Shared", ConnectionLevel.Library, TenantId: _tenant))).Id;
    }

    [TestCleanup]
    public async Task CleanupAsync()
    {
        await _factory.DisposeAsync();
        SqliteConnection.ClearAllPools();
        _library.Dispose();
    }

    [TestMethod]
    public async Task TheLibrarysRulesAreSetAndReadBackWithTheirNames()
    {
        var empty = await RulesAsync(_alice, _id);
        using var set = await PutAsync(
            _alice,
            _id,
            new("Photos", _users, null, FolderLevel.Read, "id:photos"),
            new("Photos/Trips", null, _bobId, FolderLevel.Write, null)
        );
        var read = await RulesAsync(_alice, _id);

        Assert.IsEmpty(empty);
        Assert.AreEqual(HttpStatusCode.OK, set.StatusCode, await set.Content.ReadAsStringAsync(Cancel));
        var answered = (await set.Content.ReadFromJsonAsync<ConnectionFolderRuleView[]>(Json, Cancel))!;
        CollectionAssert.AreEqual(answered.Select(r => r.Id).ToArray(), read.Select(r => r.Id).ToArray());
        var group = read.Single(r => r.GroupId == _users);
        Assert.AreEqual(
            ("Photos", "Users", FolderLevel.Read, "id:photos"),
            (group.Path, group.Name, group.Level, group.ItemId)
        );
        var bob = read.Single(r => r.UserId == _bobId);
        Assert.AreEqual(("Photos/Trips", "bob", FolderLevel.Write), (bob.Path, bob.Name, bob.Level));

        using var cleared = await PutAsync(_alice, _id);
        Assert.AreEqual(HttpStatusCode.OK, cleared.StatusCode);
        Assert.IsEmpty(await RulesAsync(_alice, _id));
    }

    [TestMethod]
    public async Task OnlyThoseManagingTheLibrarysConnectionsReachTheRoutes()
    {
        using var rules = await _bob.GetAsync(Base(_id) + "/folder-rules", Cancel);
        using var put = await PutAsync(_bob, _id, Hide("Photos"));
        using var folders = await _bob.GetAsync(Base(_id) + "/folders", Cancel);

        Assert.AreEqual(HttpStatusCode.Forbidden, rules.StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden, put.StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden, folders.StatusCode);
        Assert.IsEmpty(await RulesAsync(_admin, _id));
    }

    [TestMethod]
    public async Task AConnectionTheLibraryDoesNotReachIsNotFound()
    {
        var accounts = _factory.Services.GetRequiredService<AccessService>();
        var other = (await accounts.CreateTenantAsync(new TenantInput("Other"), _library.Root, Cancel)).Value!;
        var others = await MakeAsync(new(FakeProvider.FakeKind, "Theirs", ConnectionLevel.Library, TenantId: other.Id));
        var server = await MakeAsync(new(FakeProvider.FakeKind, "Server's", ConnectionLevel.Server));
        var own = await MakeAsync(new(FakeProvider.FakeKind, "Bob's", ConnectionLevel.Account, OwnerId: _bobId));

        foreach (var id in new[] { others.Id, server.Id, own.Id, 999 })
        {
            using var rules = await _alice.GetAsync(Base(id) + "/folder-rules", Cancel);
            using var put = await PutAsync(_alice, id, Hide("Photos"));
            using var folders = await _alice.GetAsync(Base(id) + "/folders", Cancel);
            Assert.AreEqual(HttpStatusCode.NotFound, rules.StatusCode, $"{id}");
            Assert.AreEqual(HttpStatusCode.NotFound, put.StatusCode, $"{id}");
            Assert.AreEqual(HttpStatusCode.NotFound, folders.StatusCode, $"{id}");
        }

        // Offered to the library, the server's connection is reached.
        var offered = await _admin.PutAsJsonAsync(
            Base(server.Id) + "/libraries",
            new ConnectionLibrariesInput(true, []),
            Json,
            Cancel
        );
        Assert.AreEqual(HttpStatusCode.OK, offered.StatusCode);
        Assert.IsEmpty(await RulesAsync(_alice, server.Id));
    }

    [TestMethod]
    public async Task WhatMakesARuleMeaninglessIsAValidationAnswer()
    {
        var accounts = _factory.Services.GetRequiredService<AccessService>();
        var other = (await accounts.CreateTenantAsync(new TenantInput("Other"), _library.Root, Cancel)).Value!;
        var otherUsers = (await accounts.GetBuiltInGroupAsync(other.Id, BuiltInGroup.Users, Cancel))!.Id;
        var bad = new (string Key, ConnectionRuleInput Rule)[]
        {
            ("path", new("a/../b", _users, null, FolderLevel.Read, null)),
            ("principal", new("a", _users, _bobId, FolderLevel.Read, null)),
            ("groupId", new("a", otherUsers, null, FolderLevel.Read, null)),
        };

        foreach (var (key, rule) in bad)
        {
            using var answer = await PutAsync(_alice, _id, rule);
            Assert.AreEqual(HttpStatusCode.BadRequest, answer.StatusCode, key);
            var problem = (await answer.Content.ReadFromJsonAsync<ValidationProblemDetails>(Json, Cancel))!;
            Assert.IsTrue(problem.Errors.ContainsKey(key), key);
        }
        using var twins = await PutAsync(
            _alice,
            _id,
            new("Photos", _users, null, FolderLevel.Read, null),
            new("photos", _users, null, FolderLevel.Write, null)
        );
        Assert.AreEqual(HttpStatusCode.BadRequest, twins.StatusCode);
        Assert.IsEmpty(await RulesAsync(_alice, _id));
    }

    [TestMethod]
    public async Task ThePickerListsTheFoldersOnlyInsideTheConnection()
    {
        var root = await FoldersAsync("");
        var photos = await FoldersAsync("Photos");

        Assert.AreEqual(("", null), (root.Path, root.Parent));
        CollectionAssert.AreEqual(new[] { "Music", "Photos" }, root.Folders.Select(f => f.Name).ToArray());
        Assert.AreEqual(("Photos", ""), (photos.Path, photos.Parent));
        CollectionAssert.AreEqual(
            new[] { "Photos/Family", "Photos/Trips" },
            photos.Folders.Select(f => f.Path).ToArray()
        );
        foreach (var path in new[] { "..", "../x", "Nowhere", "notes.txt" })
        {
            using var answer = await _alice.GetAsync(Folders(path), Cancel);
            Assert.AreEqual(HttpStatusCode.NotFound, answer.StatusCode, path);
        }
    }

    private async Task<ConnectionInfo> MakeAsync(ConnectionDraft draft)
    {
        var made = await _factory
            .Services.GetRequiredService<ConnectionService>()
            .CreateAsync(draft, new ConnectionGrant("acct", "acct@fake", "id:acct"), null, Cancel);
        Assert.AreEqual(AccessStatus.Ok, made.Status);
        return made.Value!;
    }

    private static string Base(int id) => $"/api/connections/{id}";

    private string Folders(string path) => $"{Base(_id)}/folders?path={Uri.EscapeDataString(path)}";

    private async Task<FolderPicker> FoldersAsync(string path) =>
        (await _alice.GetFromJsonAsync<FolderPicker>(Folders(path), Json, Cancel))!;

    private async Task<ConnectionFolderRuleView[]> RulesAsync(HttpClient client, int id) =>
        (await client.GetFromJsonAsync<ConnectionFolderRuleView[]>(Base(id) + "/folder-rules", Json, Cancel))!;

    private ConnectionRuleInput Hide(string path) => new(path, _users, null, FolderLevel.Hidden, null);

    private Task<HttpResponseMessage> PutAsync(HttpClient client, int id, params ConnectionRuleInput[] rules) =>
        client.PutAsJsonAsync(Base(id) + "/folder-rules", new ConnectionFolderRulesInput(rules), Json, Cancel);
}
