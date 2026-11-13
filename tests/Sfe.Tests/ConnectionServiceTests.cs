using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Sfe.Core.Access;
using Sfe.Core.Connections;
using Sfe.Tests.Infrastructure;

namespace Sfe.Tests;

/// <summary>
/// <see cref="ConnectionService"/> and <see cref="ConnectionSharing"/>: connections made, changed, signed in again,
/// opened, shared and removed, their credentials only ever stored protected.
/// </summary>
[TestClass]
public sealed class ConnectionServiceTests
{
    private TestLibrary _library = null!;
    private ConnectionWorld _world = null!;

    public TestContext TestContext { get; set; } = null!;

    private CancellationToken Token => TestContext.CancellationToken;

    [TestInitialize]
    public async Task InitializeAsync()
    {
        _library = new TestLibrary();
        _world = await ConnectionWorld.CreateAsync(_library, Token);
    }

    [TestCleanup]
    public async Task CleanupAsync()
    {
        await _world.DisposeAsync();
        SqliteConnection.ClearAllPools();
        _library.Dispose();
    }

    [TestMethod]
    public async Task AConnectionKeepsItsCredentialsProtectedAndOpensWithThem()
    {
        _world.Provider.Account("alice").WithFile("notes.txt", "hello");
        var made = await _world.CreateAsync(Own(_world.Alice, "  Mine  ", "/Work//"), "alice", Token);

        Assert.AreEqual("Mine", made.Name);
        Assert.AreEqual("Work", made.RootPath);
        Assert.AreEqual("alice@fake", made.Account);
        Assert.AreEqual(ConnectionState.Ready, made.State);
        await using (var db = _world.Db())
        {
            var row = await db.Connections.SingleAsync(Token);
            Assert.AreEqual(FakeProtector.Prefix + "alice", row.Credentials);
            Assert.AreEqual("id:alice", row.AccountId);
            Assert.AreEqual(_world.Admin, row.CreatedById);
        }

        _world.Provider.Account("alice").WithFile("Work/inside.txt", "in");
        var opened = await _world.Connections.OpenAsync(made.Id, Token);
        Assert.AreEqual(AccessStatus.Ok, opened.Status);
        var page = await opened.Value!.ListAsync("", null, Token);
        CollectionAssert.AreEqual(new[] { "inside.txt" }, page.Items.Select(i => i.Name).ToArray());

        CollectionAssert.AreEqual(
            new[] { made.Id },
            (await _world.Connections.GetOwnedAsync(_world.Alice, Token)).Select(c => c.Id).ToArray()
        );
        Assert.IsEmpty(await _world.Connections.GetOwnedAsync(_world.Bob, Token));
    }

    [TestMethod]
    public async Task WhatIsNotMeaningfulOrNotAllowedIsRefused()
    {
        var grant = new ConnectionGrant("alice", "alice@fake", "id:alice");
        (ConnectionDraft Draft, string Key)[] wrong =
        [
            (new("nope", "N", ConnectionLevel.Account, _world.Alice), "kind"),
            (new(FakeProvider.FakeKind, " ", ConnectionLevel.Account, _world.Alice), "name"),
            (new(FakeProvider.FakeKind, new string('n', 201), ConnectionLevel.Account, _world.Alice), "name"),
            (new(FakeProvider.FakeKind, "N", ConnectionLevel.Account, _world.Alice, RootPath: "a/../b"), "rootPath"),
            (new(FakeProvider.FakeKind, "N", ConnectionLevel.Account), "ownerId"),
            (new(FakeProvider.FakeKind, "N", ConnectionLevel.Account, OwnerId: 999), "ownerId"),
            (new(FakeProvider.FakeKind, "N", ConnectionLevel.Library), "tenantId"),
            (new(FakeProvider.FakeKind, "N", ConnectionLevel.Library, TenantId: 999), "tenantId"),
            (new(FakeProvider.FakeKind, "N", (ConnectionLevel)7), "level"),
        ];
        foreach (var (draft, key) in wrong)
        {
            var result = await _world.Connections.CreateAsync(draft, grant, null, Token);
            Assert.AreEqual(AccessStatus.Invalid, result.Status, $"{draft}");
            Assert.IsTrue(result.Errors!.ContainsKey(key), $"{draft}: {string.Join(", ", result.Errors.Keys)}");
        }

        await using var other = _world.WithSettings(
            _library,
            new Dictionary<string, string?> { ["Sfe:Connections:Providers:0"] = "dropbox" }
        );
        var refused = await other.Connections.CreateAsync(Own(_world.Alice, "N"), grant, null, Token);
        Assert.IsTrue(refused.Errors!.ContainsKey("kind"));
        await using var db = _world.Db();
        Assert.IsFalse(await db.Connections.AnyAsync(Token));
    }

    [TestMethod]
    public async Task SigningInAgainKeepsTheConnectionAndTellsAnotherAccount()
    {
        var made = await _world.CreateAsync(Library("Shared"), "first", Token);
        await _world.Sharing.SetGroupsAsync(
            made.Id,
            _world.Tenant,
            Levels((_world.Users, FolderLevel.Read)),
            null,
            Token
        );
        await _world.Connections.MarkNeedsReconnectingAsync(made.Id, Token);
        Assert.AreEqual(ConnectionState.NeedsReconnecting, (await _world.Connections.FindAsync(made.Id, Token))!.State);
        Assert.AreEqual(
            ConnectionService.NeedsReconnectingProblem,
            (await _world.Connections.OpenAsync(made.Id, Token)).Problem
        );

        var same = await _world.Connections.ReauthorizeAsync(
            made.Id,
            new("first", "first@fake", "id:first"),
            null,
            Token
        );
        Assert.IsFalse(same.Value!.OtherAccount);
        Assert.AreEqual(ConnectionState.Ready, same.Value.Connection.State);

        var other = await _world.Connections.ReauthorizeAsync(
            made.Id,
            new("second", "second@fake", "id:second"),
            null,
            Token
        );
        Assert.IsTrue(other.Value!.OtherAccount);
        Assert.AreEqual(made.Id, other.Value.Connection.Id);
        Assert.AreEqual("second@fake", other.Value.Connection.Account);
        Assert.HasCount(1, other.Value.Connection.Groups);
        Assert.AreEqual(
            AccessStatus.NotFound,
            (await _world.Connections.ReauthorizeAsync(999, new("x", "x"), null, Token)).Status
        );
    }

    /// <summary>A backup restored without its keys folder: the connection asks to be signed in to again.</summary>
    [TestMethod]
    public async Task CredentialsThatCannotBeReadBackAskForANewSignIn()
    {
        var made = await _world.CreateAsync(Own(_world.Alice, "Mine"), "alice", Token);
        await using (var db = _world.Db())
            await db.Connections.ExecuteUpdateAsync(s => s.SetProperty(c => c.Credentials, "other keys"), Token);

        var opened = await _world.Connections.OpenAsync(made.Id, Token);

        Assert.AreEqual(AccessStatus.Conflict, opened.Status);
        Assert.AreEqual(ConnectionService.NeedsReconnectingProblem, opened.Problem);
        Assert.AreEqual(ConnectionState.NeedsReconnecting, (await _world.Connections.FindAsync(made.Id, Token))!.State);
    }

    [TestMethod]
    public async Task ADisabledConnectionIsKeptButNotOpened()
    {
        var made = await _world.CreateAsync(Library("Shared"), "shared", Token);

        var changed = await _world.Connections.UpdateAsync(
            made.Id,
            new(Name: "Renamed", Disabled: true, ReadOnly: true, RootPath: "Photos"),
            null,
            Token
        );

        Assert.AreEqual(
            ("Renamed", true, true, "Photos"),
            (changed.Value!.Name, changed.Value.Disabled, changed.Value.ReadOnly, changed.Value.RootPath)
        );
        Assert.AreEqual(
            ConnectionService.UnusableProblem,
            (await _world.Connections.OpenAsync(made.Id, Token)).Problem
        );
        Assert.AreEqual(
            AccessStatus.Invalid,
            (await _world.Connections.UpdateAsync(made.Id, new(Name: ""), null, Token)).Status
        );
        Assert.AreEqual(AccessStatus.NotFound, (await _world.Connections.UpdateAsync(999, new(), null, Token)).Status);
    }

    [TestMethod]
    public async Task AServerConnectionIsOfferedToLibrariesWhichChooseTheirGroups()
    {
        var made = await _world.CreateAsync(new(FakeProvider.FakeKind, "Server", ConnectionLevel.Server), "srv", Token);
        Assert.AreEqual(
            AccessStatus.NotFound,
            (await GroupsAsync(made.Id, _world.Tenant, (_world.Users, FolderLevel.Read))).Status
        );

        await _world.Sharing.SetTenantsAsync(made.Id, false, [_world.Tenant, _world.OtherTenant], null, Token);
        await GroupsAsync(made.Id, _world.Tenant, (_world.Users, FolderLevel.Read), (_world.Team, FolderLevel.Write));
        await GroupsAsync(made.Id, _world.OtherTenant, (_world.OtherUsers, FolderLevel.Read));
        var wrongGroup = await GroupsAsync(made.Id, _world.Tenant, (_world.OtherUsers, FolderLevel.Read));
        Assert.IsTrue(wrongGroup.Errors!.ContainsKey("groupId"));
        Assert.AreEqual(
            AccessStatus.Invalid,
            (await GroupsAsync(made.Id, _world.Tenant, (_world.Users, FolderLevel.Hidden))).Status
        );

        // Replacing one library's groups leaves the other's; a kept row changes its level in place.
        var replaced = await GroupsAsync(made.Id, _world.Tenant, (_world.Team, FolderLevel.Read));
        CollectionAssert.AreEqual(
            new[] { new ConnectionGroupInfo(_world.Team, _world.Tenant, FolderLevel.Read) },
            replaced.Value!.Groups.ToArray()
        );
        Assert.HasCount(2, (await _world.Connections.FindAsync(made.Id, Token))!.Groups);

        var listed = await _world.Connections.GetOfLibraryAsync(_world.OtherTenant, Token);
        Assert.AreEqual(_world.OtherUsers, listed.Single().Groups.Single().GroupId);

        // No longer offered to the other library: its groups' rows go.
        var narrowed = await _world.Sharing.SetTenantsAsync(made.Id, false, [_world.Tenant], null, Token);
        CollectionAssert.AreEqual(new[] { _world.Tenant }, narrowed.Value!.TenantIds.ToArray());
        Assert.AreEqual(_world.Team, narrowed.Value.Groups.Single().GroupId);
        Assert.IsEmpty(await _world.Connections.GetOfLibraryAsync(_world.OtherTenant, Token));

        var all = await _world.Sharing.SetTenantsAsync(made.Id, true, [], null, Token);
        Assert.IsTrue(all.Value!.AllTenants);
        Assert.HasCount(1, await _world.Connections.GetOfLibraryAsync(_world.OtherTenant, Token));
        Assert.AreEqual(
            AccessStatus.Invalid,
            (await _world.Sharing.SetTenantsAsync(made.Id, false, [999], null, Token)).Status
        );
    }

    [TestMethod]
    public async Task OnlyAServerConnectionIsOfferedAndAnAccountOneHasNoGroups()
    {
        var own = await _world.CreateAsync(Own(_world.Alice, "Mine"), "alice", Token);
        var library = await _world.CreateAsync(Library("Shared"), "shared", Token);

        Assert.AreEqual(
            AccessStatus.Invalid,
            (await _world.Sharing.SetTenantsAsync(library.Id, true, [], null, Token)).Status
        );
        Assert.AreEqual(
            AccessStatus.NotFound,
            (await GroupsAsync(own.Id, _world.Tenant, (_world.Users, FolderLevel.Read))).Status
        );
        Assert.AreEqual(
            AccessStatus.NotFound,
            (await GroupsAsync(library.Id, _world.OtherTenant, (_world.OtherUsers, FolderLevel.Read))).Status
        );
    }

    [TestMethod]
    public async Task ARemovedConnectionTakesItsRowsAndLeavesTheStorage()
    {
        _world.Provider.Account("srv").WithFile("kept.txt", "kept");
        var made = await _world.CreateAsync(new(FakeProvider.FakeKind, "Server", ConnectionLevel.Server), "srv", Token);
        await _world.Sharing.SetTenantsAsync(made.Id, false, [_world.Tenant], null, Token);
        await GroupsAsync(made.Id, _world.Tenant, (_world.Users, FolderLevel.Write));

        Assert.AreEqual(AccessStatus.Ok, await _world.Connections.RemoveAsync(made.Id, _world.Admin, Token));
        Assert.AreEqual(AccessStatus.NotFound, await _world.Connections.RemoveAsync(made.Id, _world.Admin, Token));

        await using var db = _world.Db();
        Assert.IsFalse(await db.Connections.AnyAsync(Token));
        Assert.IsFalse(await db.ConnectionGroups.AnyAsync(Token));
        Assert.IsFalse(await db.ConnectionTenants.AnyAsync(Token));
        Assert.AreEqual("kept", _world.Provider.Account("srv").TextOf("kept.txt"));
    }

    private ConnectionDraft Own(int owner, string name, string? root = null) =>
        new(FakeProvider.FakeKind, name, ConnectionLevel.Account, OwnerId: owner, RootPath: root);

    private ConnectionDraft Library(string name) =>
        new(FakeProvider.FakeKind, name, ConnectionLevel.Library, TenantId: _world.Tenant);

    private Task<AccessResult<ConnectionInfo>> GroupsAsync(
        int id,
        int tenant,
        params (int Group, FolderLevel Level)[] levels
    ) => _world.Sharing.SetGroupsAsync(id, tenant, Levels(levels), null, Token);

    private static Dictionary<int, FolderLevel> Levels(params (int Group, FolderLevel Level)[] levels) =>
        levels.ToDictionary(l => l.Group, l => l.Level);
}
