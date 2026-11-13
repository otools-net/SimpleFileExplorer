using Microsoft.Data.Sqlite;
using Sfe.Core.Access;
using Sfe.Core.Connections;
using Sfe.Core.Data;
using Sfe.Core.Options;
using Sfe.Tests.Infrastructure;

namespace Sfe.Tests;

/// <summary>
/// <see cref="ConnectionAccess"/>: who uses which connection and how far, by level, groups, settings and switches.
/// </summary>
[TestClass]
public sealed class ConnectionAccessTests
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

    /// <summary>An account's connection is its owner's in every library; administrators never open it.</summary>
    [TestMethod]
    public async Task AnAccountConnectionIsItsOwnersOnly()
    {
        var own = await _world.CreateAsync(
            new(FakeProvider.FakeKind, "Mine", ConnectionLevel.Account, OwnerId: _world.Alice),
            "alice",
            Token
        );

        Assert.AreEqual(FolderLevel.Write, await LevelAsync(_world.Alice, own.Id));
        Assert.AreEqual(FolderLevel.Write, await LevelAsync(_world.Alice, own.Id, _world.OtherTenant));
        Assert.IsNull(await LevelAsync(_world.Bob, own.Id));
        Assert.IsNull(await LevelAsync(_world.Admin, own.Id));
        var guest = new ConnectionSubject(_world.Tenant, _world.Alice, false, IsGuest: true);
        Assert.IsNull(await _world.Access.LevelAsync(guest, own.Id, Token));
        Assert.IsNull(await _world.Access.LevelAsync(new(_world.Tenant, null, false, true), own.Id, Token));
    }

    [TestMethod]
    public async Task ALibraryConnectionIsUsedByItsGroupsAtTheirBestLevel()
    {
        var shared = await _world.CreateAsync(
            new(FakeProvider.FakeKind, "Shared", ConnectionLevel.Library, TenantId: _world.Tenant),
            "shared",
            Token
        );
        Assert.IsNull(await LevelAsync(_world.Alice, shared.Id));
        Assert.AreEqual(FolderLevel.Write, await LevelAsync(_world.Admin, shared.Id));

        await SetGroupsAsync(
            shared.Id,
            _world.Tenant,
            (_world.Users, FolderLevel.Read),
            (_world.Team, FolderLevel.Write)
        );
        Assert.AreEqual(FolderLevel.Read, await LevelAsync(_world.Alice, shared.Id));
        Assert.AreEqual(FolderLevel.Write, await LevelAsync(_world.Bob, shared.Id));
        Assert.IsNull(await LevelAsync(_world.Carol, shared.Id, _world.OtherTenant));
        // Not from another library, even for its administrators.
        Assert.IsNull(await LevelAsync(_world.Admin, shared.Id, _world.OtherTenant));

        await _world.Connections.UpdateAsync(shared.Id, new(ReadOnly: true), null, Token);
        Assert.AreEqual(FolderLevel.Read, await LevelAsync(_world.Bob, shared.Id));
        Assert.AreEqual(FolderLevel.Read, await LevelAsync(_world.Admin, shared.Id));

        await _world.Connections.UpdateAsync(shared.Id, new(Disabled: true), null, Token);
        Assert.IsNull(await LevelAsync(_world.Bob, shared.Id));
        Assert.IsNull(await LevelAsync(_world.Admin, shared.Id));
    }

    [TestMethod]
    public async Task AServerConnectionIsUsedInTheLibrariesItIsOfferedTo()
    {
        var server = await _world.CreateAsync(
            new(FakeProvider.FakeKind, "Server", ConnectionLevel.Server),
            "srv",
            Token
        );
        Assert.IsNull(await LevelAsync(_world.Admin, server.Id));

        await _world.Sharing.SetTenantsAsync(server.Id, false, [_world.OtherTenant], null, Token);
        await SetGroupsAsync(server.Id, _world.OtherTenant, (_world.OtherUsers, FolderLevel.Write));
        Assert.AreEqual(FolderLevel.Write, await LevelAsync(_world.Carol, server.Id, _world.OtherTenant));
        Assert.AreEqual(FolderLevel.Write, await LevelAsync(_world.Admin, server.Id, _world.OtherTenant));
        Assert.IsNull(await LevelAsync(_world.Admin, server.Id));

        await _world.Sharing.SetTenantsAsync(server.Id, true, [], null, Token);
        Assert.AreEqual(FolderLevel.Write, await LevelAsync(_world.Admin, server.Id));
        Assert.IsNull(await LevelAsync(_world.Alice, server.Id));
    }

    [TestMethod]
    public async Task TheConnectionsOneMayUseAreListedByName()
    {
        await _world.CreateAsync(
            new(FakeProvider.FakeKind, "b-mine", ConnectionLevel.Account, OwnerId: _world.Bob),
            "bob",
            Token
        );
        var shared = await _world.CreateAsync(
            new(FakeProvider.FakeKind, "A-team", ConnectionLevel.Library, TenantId: _world.Tenant),
            "team",
            Token
        );
        await _world.CreateAsync(
            new(FakeProvider.FakeKind, "c-alice", ConnectionLevel.Account, OwnerId: _world.Alice),
            "alice",
            Token
        );
        await SetGroupsAsync(shared.Id, _world.Tenant, (_world.Team, FolderLevel.Read));

        var usable = await _world.Access.UsableAsync(_world.Subject(_world.Bob), Token);

        CollectionAssert.AreEqual(new[] { "A-team", "b-mine" }, usable.Select(u => u.Name).ToArray());
        CollectionAssert.AreEqual(
            new[] { FolderLevel.Read, FolderLevel.Write },
            usable.Select(u => u.Access).ToArray()
        );
    }

    [TestMethod]
    public async Task TheServerSettingsTurnConnectionsOff()
    {
        var own = await _world.CreateAsync(
            new(FakeProvider.FakeKind, "Mine", ConnectionLevel.Account, OwnerId: _world.Alice),
            "alice",
            Token
        );
        await using (
            var off = _world.WithSettings(
                _library,
                new Dictionary<string, string?> { ["Sfe:Connections:Enabled"] = "false" }
            )
        )
        {
            Assert.IsNull(await off.Access.LevelAsync(_world.Subject(_world.Alice), own.Id, Token));
            Assert.IsEmpty(await off.Access.UsableAsync(_world.Subject(_world.Alice), Token));
            Assert.AreEqual(
                ConnectionService.UnusableProblem,
                (await off.Connections.OpenAsync(own.Id, Token)).Problem
            );
        }
        await using (
            var otherKinds = _world.WithSettings(
                _library,
                new Dictionary<string, string?> { ["Sfe:Connections:Providers:0"] = "dropbox" }
            )
        )
            Assert.IsNull(await otherKinds.Access.LevelAsync(_world.Subject(_world.Alice), own.Id, Token));
    }

    /// <summary>A level taken out of <c>Connections:Levels</c> leaves its connections unusable.</summary>
    [TestMethod]
    public void ALevelTakenOutOfTheSettingsIsNotUsed()
    {
        var connection = new Connection
        {
            Kind = FakeProvider.FakeKind,
            Level = ConnectionLevel.Account,
            OwnerId = 1,
        };
        var owner = new ConnectionSubject(1, 1, false, false);

        Assert.AreEqual(FolderLevel.Write, ConnectionAccess.Decide(connection, owner, null, new ConnectionOptions()));
        Assert.IsNull(
            ConnectionAccess.Decide(
                connection,
                owner,
                null,
                new ConnectionOptions { Levels = [ConnectionOptions.LibraryLevel] }
            )
        );
    }

    private Task<FolderLevel?> LevelAsync(int userId, int connectionId, int? tenantId = null) =>
        _world.Access.LevelAsync(_world.Subject(userId, tenantId), connectionId, Token);

    private Task<AccessResult<ConnectionInfo>> SetGroupsAsync(
        int id,
        int tenant,
        params (int Group, FolderLevel Level)[] levels
    ) => _world.Sharing.SetGroupsAsync(id, tenant, levels.ToDictionary(l => l.Group, l => l.Level), null, Token);
}
