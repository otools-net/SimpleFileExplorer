using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Otools.Net.Sfe.Core.Access;
using Otools.Net.Sfe.Core.Connections;
using Otools.Net.Sfe.Tests.Infrastructure;

namespace Otools.Net.Sfe.Tests;

/// <summary>
/// <see cref="ConnectionRuleService"/>: each library replaces its own folder rules in a connection it reaches;
/// what makes them meaningless is refused.
/// </summary>
[TestClass]
public sealed class ConnectionRuleServiceTests
{
    private TestLibrary _library = null!;
    private ConnectionWorld _world = null!;

    public TestContext TestContext { get; set; } = null!;

    private CancellationToken Token => TestContext.CancellationToken;

    private ConnectionRuleService Rules => _world.Services.GetRequiredService<ConnectionRuleService>();

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
    public async Task ALibraryReplacesItsRulesKeepingThoseThatStayInPlace()
    {
        var made = await _world.CreateAsync(Library("Shared"), "shared", Token);
        var changes = _world.Services.GetRequiredService<AccessChanges>();
        var version = changes.Version;

        var first = await SetAsync(
            made.Id,
            _world.Tenant,
            Group(_world.Users, "/Private/", FolderLevel.Hidden),
            User(_world.Bob, "Work", FolderLevel.Write, "id:work")
        );
        Assert.AreEqual(AccessStatus.Ok, first.Status);
        Assert.IsGreaterThan(version, changes.Version);
        var privateRule = first.Value!.Single(r => r.GroupId == _world.Users);
        Assert.AreEqual("Private", privateRule.Path);
        Assert.AreEqual("id:work", first.Value!.Single(r => r.UserId == _world.Bob).ItemId);

        // The same folder, as the provider compares names, keeps its row; Bob's rule goes.
        var second = await SetAsync(
            made.Id,
            _world.Tenant,
            Group(_world.Users, "private", FolderLevel.Read),
            Group(_world.Team, "", FolderLevel.Read)
        );
        Assert.AreEqual(AccessStatus.Ok, second.Status);
        var kept = second.Value!.Single(r => r.GroupId == _world.Users);
        Assert.AreEqual(privateRule.Id, kept.Id);
        Assert.AreEqual(("private", FolderLevel.Read), (kept.Path, kept.Level));
        CollectionAssert.AreEquivalent(
            second.Value!.ToArray(),
            (await Rules.GetAsync(made.Id, _world.Tenant, Token)).ToArray()
        );

        Assert.AreEqual(AccessStatus.Ok, (await SetAsync(made.Id, _world.Tenant)).Status);
        Assert.IsEmpty(await Rules.GetAsync(made.Id, _world.Tenant, Token));
    }

    [TestMethod]
    public async Task WhatMakesARuleMeaninglessIsRefused()
    {
        var made = await _world.CreateAsync(Library("Shared"), "shared", Token);

        string[] KeysOf(AccessResult<IReadOnlyList<ConnectionRuleInfo>> result)
        {
            Assert.AreEqual(AccessStatus.Invalid, result.Status);
            return [.. result.Errors!.Keys];
        }

        var bad = new (string Key, ConnectionRuleInput Rule)[]
        {
            ("path", Group(_world.Users, "a/../b", FolderLevel.Read)),
            ("path", Group(_world.Users, "a\\b", FolderLevel.Read)),
            ("path", Group(_world.Users, new string('x', RemotePaths.MaxPathLength + 1), FolderLevel.Read)),
            ("principal", new ConnectionRuleInput("a", _world.Users, _world.Bob, FolderLevel.Read, null)),
            ("principal", new ConnectionRuleInput("a", null, null, FolderLevel.Read, null)),
            ("level", Group(_world.Users, "a", (FolderLevel)7)),
            ("itemId", new ConnectionRuleInput("a", _world.Users, null, FolderLevel.Read, new string('i', 201))),
            // A group of the other library, an account in no group of this one.
            ("groupId", Group(_world.OtherUsers, "a", FolderLevel.Read)),
            ("userId", User(_world.Carol, "a", FolderLevel.Read)),
        };
        foreach (var (key, rule) in bad)
            CollectionAssert.Contains(KeysOf(await SetAsync(made.Id, _world.Tenant, rule)), key, key);

        var twins = await SetAsync(
            made.Id,
            _world.Tenant,
            Group(_world.Users, "A", FolderLevel.Read),
            Group(_world.Users, "a", FolderLevel.Write)
        );
        CollectionAssert.Contains(KeysOf(twins), "path");

        var tooMany = Enumerable
            .Range(0, ConnectionRuleService.MaxRules + 1)
            .Select(i => Group(_world.Users, $"f{i}", FolderLevel.Read))
            .ToArray();
        CollectionAssert.Contains(KeysOf(await SetAsync(made.Id, _world.Tenant, tooMany)), "rules");
        var enough = await SetAsync(made.Id, _world.Tenant, tooMany[..ConnectionRuleService.MaxRules]);
        Assert.AreEqual(AccessStatus.Ok, enough.Status);
        Assert.HasCount(ConnectionRuleService.MaxRules, enough.Value!);
    }

    [TestMethod]
    public async Task AnAccountConnectionHasNoRulesAndAnotherLibrarysIsNotFound()
    {
        var own = await _world.CreateAsync(
            new(FakeProvider.FakeKind, "Mine", ConnectionLevel.Account, OwnerId: _world.Alice),
            "alice",
            Token
        );
        var library = await _world.CreateAsync(Library("Shared"), "shared", Token);
        var server = await _world.CreateAsync(new(FakeProvider.FakeKind, "Srv", ConnectionLevel.Server), "srv", Token);

        var rule = Group(_world.Users, "a", FolderLevel.Read);
        Assert.AreEqual(AccessStatus.Invalid, (await SetAsync(own.Id, _world.Tenant, rule)).Status);
        var otherRule = Group(_world.OtherUsers, "a", FolderLevel.Read);
        Assert.AreEqual(AccessStatus.NotFound, (await SetAsync(library.Id, _world.OtherTenant, otherRule)).Status);
        Assert.AreEqual(AccessStatus.NotFound, (await SetAsync(server.Id, _world.Tenant, rule)).Status);
        Assert.AreEqual(AccessStatus.NotFound, (await SetAsync(999, _world.Tenant, rule)).Status);
    }

    [TestMethod]
    public async Task EachLibrarySetsItsOwnAndLosesThemWhenNoLongerOffered()
    {
        var made = await _world.CreateAsync(new(FakeProvider.FakeKind, "Srv", ConnectionLevel.Server), "srv", Token);
        await _world.Sharing.SetTenantsAsync(made.Id, false, [_world.Tenant, _world.OtherTenant], null, Token);
        await SetAsync(made.Id, _world.Tenant, Group(_world.Users, "a", FolderLevel.Hidden));
        await SetAsync(made.Id, _world.OtherTenant, Group(_world.OtherUsers, "a", FolderLevel.Write));

        // Replacing one library's rules leaves the other's.
        await SetAsync(made.Id, _world.Tenant, Group(_world.Team, "b", FolderLevel.Read));
        var others = await Rules.GetAsync(made.Id, _world.OtherTenant, Token);
        Assert.AreEqual((_world.OtherUsers, FolderLevel.Write), (others.Single().GroupId, others.Single().Level));

        await _world.Sharing.SetTenantsAsync(made.Id, false, [_world.Tenant], null, Token);
        Assert.IsEmpty(await Rules.GetAsync(made.Id, _world.OtherTenant, Token));
        Assert.HasCount(1, await Rules.GetAsync(made.Id, _world.Tenant, Token));

        // Offered to every library: nobody loses theirs.
        await _world.Sharing.SetTenantsAsync(made.Id, true, [], null, Token);
        Assert.HasCount(1, await Rules.GetAsync(made.Id, _world.Tenant, Token));
    }

    [TestMethod]
    public async Task ARemovedConnectionTakesItsRules()
    {
        var made = await _world.CreateAsync(Library("Shared"), "shared", Token);
        await SetAsync(made.Id, _world.Tenant, Group(_world.Users, "a", FolderLevel.Read));

        Assert.AreEqual(AccessStatus.Ok, await _world.Connections.RemoveAsync(made.Id, _world.Admin, Token));
        await using var db = _world.Db();
        Assert.IsFalse(await db.ConnectionFolderRules.AnyAsync(Token));
    }

    private ConnectionDraft Library(string name) =>
        new(FakeProvider.FakeKind, name, ConnectionLevel.Library, TenantId: _world.Tenant);

    private static ConnectionRuleInput Group(int groupId, string path, FolderLevel level) =>
        new(path, groupId, null, level, null);

    private static ConnectionRuleInput User(int userId, string path, FolderLevel level, string? itemId = null) =>
        new(path, null, userId, level, itemId);

    private Task<AccessResult<IReadOnlyList<ConnectionRuleInfo>>> SetAsync(
        int connectionId,
        int tenantId,
        params ConnectionRuleInput[] rules
    ) => Rules.SetAsync(connectionId, tenantId, rules, _world.Admin, Token);
}
