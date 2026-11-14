using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Otools.Net.Sfe.Api.Endpoints;
using Otools.Net.Sfe.Core.Access;
using Otools.Net.Sfe.Core.Connections;
using Otools.Net.Sfe.Core.Data;
using Otools.Net.Sfe.Tests.Infrastructure;

namespace Otools.Net.Sfe.Tests;

/// <summary>
/// Listing and managing connections (<c>api/connections</c>): what each session uses and manages, and the attack
/// vectors of the plan (another account's connection: 404, administrators remove it but never manage it, a library's
/// administrator and the server's settings).
/// </summary>
[TestClass]
public sealed class ConnectionRouteTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private TestLibrary _library = null!;
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _admin = null!;
    private HttpClient _alice = null!;
    private HttpClient _bob = null!;
    private int _tenant;
    private int _users;
    private int _adminId;
    private int _bobId;

    public TestContext TestContext { get; set; } = null!;

    private CancellationToken Cancel => TestContext.CancellationToken;

    [TestInitialize]
    public async Task InitializeAsync()
    {
        _library = new TestLibrary();
        _factory = ApiClients
            .CreateFactory(_library)
            .WithWebHostBuilder(builder =>
                builder.ConfigureServices(services => services.AddSingleton<IConnectionProvider>(new FakeProvider()))
            );
        _admin = await ApiClients.SetUpAsync(_factory, Cancel);
        var accounts = _factory.Services.GetRequiredService<AccessService>();
        _tenant = (await accounts.GetTenantsAsync(Cancel)).Single().Id;
        _users = (await accounts.GetBuiltInGroupAsync(_tenant, BuiltInGroup.Users, Cancel))!.Id;
        var admins = (await accounts.GetBuiltInGroupAsync(_tenant, BuiltInGroup.Administrators, Cancel))!.Id;
        _adminId = (await accounts.GetServerAdminIdsAsync(Cancel)).Single();
        await accounts.CreateUserAsync(new("alice", Password: "alice-password", GroupIds: [admins]), null, Cancel);
        _bobId = (
            await accounts.CreateUserAsync(new("bob", Password: "bob-password-1", GroupIds: [_users]), null, Cancel)
        )
            .Value!
            .Id;
        _alice = await ApiClients.SignInAsync(_factory, "alice", "alice-password", Cancel);
        _bob = await ApiClients.SignInAsync(_factory, "bob", "bob-password-1", Cancel);
    }

    [TestCleanup]
    public async Task CleanupAsync()
    {
        await _factory.DisposeAsync();
        SqliteConnection.ClearAllPools();
        _library.Dispose();
    }

    [TestMethod]
    public async Task EachSessionListsWhatItManagesAndUses()
    {
        var own = await MakeAsync(new(FakeProvider.FakeKind, "Admin's", ConnectionLevel.Account, OwnerId: _adminId));
        var server = await MakeAsync(new(FakeProvider.FakeKind, "Shared", ConnectionLevel.Server));
        var bobs = await MakeAsync(new(FakeProvider.FakeKind, "Bob's", ConnectionLevel.Account, OwnerId: _bobId));

        var admin = await ListAsync(_admin);
        var alice = await ListAsync(_alice);

        var mine = admin.Managed.Single(m => m.Connection.Id == own.Id);
        Assert.IsTrue(mine.Manage && mine.Remove);
        var others = admin.Managed.Single(m => m.Connection.Id == bobs.Id);
        Assert.IsFalse(others.Manage);
        Assert.IsTrue(others.Remove);
        Assert.IsTrue(admin.Managed.Single(m => m.Connection.Id == server.Id).Manage);
        CollectionAssert.AreEqual(new[] { own.Id }, admin.Usable.Select(u => u.Id).ToArray());

        // A library's administrator: nobody's account connection, not the server's until it is offered here.
        Assert.IsEmpty(alice.Managed);
        Assert.IsEmpty(alice.Usable);
        // Bob has none of the rights.
        Assert.AreEqual(HttpStatusCode.Forbidden, (await _bob.GetAsync($"{V1}/connections", Cancel)).StatusCode);
    }

    [TestMethod]
    public async Task AnotherAccountsConnectionIsNotFoundAndOnlyRemovedByTheServersAdministrators()
    {
        var bobs = await MakeAsync(new(FakeProvider.FakeKind, "Bob's", ConnectionLevel.Account, OwnerId: _bobId));
        var path = $"{V1}/connections/{bobs.Id}";

        using var rename = await _admin.PatchAsJsonAsync(path, new ConnectionChange(Name: "Mine now"), Json, Cancel);
        using var aliceRemove = await _alice.DeleteAsync(path, Cancel);
        using var adminRemove = await _admin.DeleteAsync(path, Cancel);

        Assert.AreEqual(HttpStatusCode.NotFound, rename.StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, aliceRemove.StatusCode);
        Assert.AreEqual(HttpStatusCode.NoContent, adminRemove.StatusCode);
        Assert.IsNull(await _factory.Services.GetRequiredService<ConnectionService>().FindAsync(bobs.Id, Cancel));
    }

    [TestMethod]
    public async Task AServerConnectionIsOfferedThenGivenToTheLibrarysGroups()
    {
        var server = await MakeAsync(new(FakeProvider.FakeKind, "Shared", ConnectionLevel.Server));
        var path = $"{V1}/connections/{server.Id}";

        using var notOffered = await _alice.PutAsJsonAsync($"{path}/groups", Groups(_users), Json, Cancel);
        using var aliceOffers = await _alice.PutAsJsonAsync($"{path}/libraries", All(), Json, Cancel);
        using var adminOffers = await _admin.PutAsJsonAsync($"{path}/libraries", All(), Json, Cancel);
        using var given = await _alice.PutAsJsonAsync($"{path}/groups", Groups(_users), Json, Cancel);
        using var renamed = await _alice.PatchAsJsonAsync(path, new ConnectionChange(Name: "x"), Json, Cancel);

        Assert.AreEqual(HttpStatusCode.NotFound, notOffered.StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden, aliceOffers.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, adminOffers.StatusCode, await adminOffers.Content.ReadAsStringAsync(Cancel));
        Assert.AreEqual(HttpStatusCode.OK, given.StatusCode, await given.Content.ReadAsStringAsync(Cancel));
        // Its groups here, but the connection is the server's: not hers to rename.
        Assert.AreEqual(HttpStatusCode.NotFound, renamed.StatusCode);
        var alice = await ListAsync(_alice);
        var listed = alice.Managed.Single();
        Assert.IsFalse(listed.Manage);
        Assert.AreEqual(_users, listed.Connection.Groups.Single().GroupId);
        // Alice administers the library, not the server: she uses it as the server's administrators would not.
        Assert.IsEmpty(alice.Usable);
    }

    [TestMethod]
    public async Task ItsManagerRenamesAndSwitchesIt()
    {
        var own = await MakeAsync(new(FakeProvider.FakeKind, "Admin's", ConnectionLevel.Account, OwnerId: _adminId));
        var path = $"{V1}/connections/{own.Id}";

        using var empty = await _admin.PatchAsJsonAsync(path, new ConnectionChange(Name: " "), Json, Cancel);
        using var changed = await _admin.PatchAsJsonAsync(
            path,
            new ConnectionChange(Name: "Renamed", ReadOnly: true),
            Json,
            Cancel
        );

        Assert.AreEqual(HttpStatusCode.BadRequest, empty.StatusCode);
        var after = (await changed.Content.ReadFromJsonAsync<ConnectionInfo>(Json, Cancel))!;
        Assert.AreEqual(("Renamed", true), (after.Name, after.ReadOnly));
        Assert.AreEqual(Otools.Net.Sfe.Core.Access.FolderLevel.Read, (await ListAsync(_admin)).Usable.Single().Access);
    }

    private async Task<ConnectionInfo> MakeAsync(ConnectionDraft draft)
    {
        var made = await _factory
            .Services.GetRequiredService<ConnectionService>()
            .CreateAsync(draft, new ConnectionGrant("x", "x@fake", "id:x"), _adminId, Cancel);
        Assert.AreEqual(AccessStatus.Ok, made.Status);
        return made.Value!;
    }

    private async Task<ConnectionsView> ListAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<ConnectionsView>($"{V1}/connections", Json, Cancel))!;

    private static ConnectionLibrariesInput All() => new(true, []);

    private static ConnectionGroupsInput Groups(int group) => new(new() { [group] = FolderLevel.Write });
}
