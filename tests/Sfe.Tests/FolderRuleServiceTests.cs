using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sfe.Core;
using Sfe.Core.Access;
using Sfe.Core.Data;
using Sfe.Core.Paths;
using Sfe.Tests.Infrastructure;

namespace Sfe.Tests;

/// <summary>Folder rules in the accounts database: set, moved with their folders, and applied to the rights.</summary>
[TestClass]
public sealed class FolderRuleServiceTests
{
    private TestLibrary _library = null!;
    private ServiceProvider _services = null!;
    private FolderRuleService _rules = null!;
    private AccessService _accounts = null!;
    private int _tenant;
    private int _users;
    private int _guests;
    private int _alice;

    public TestContext TestContext { get; set; } = null!;

    private CancellationToken Token => TestContext.CancellationToken;

    [TestInitialize]
    public async Task InitializeAsync()
    {
        _library = new TestLibrary();
        var settings = _library.Settings();
        settings["Sfe:Security:AnonymousGuests"] = "true";
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        _services = new ServiceCollection().AddLogging().AddSfeAccess(configuration).BuildServiceProvider();
        await _services.GetRequiredService<AccessDatabaseInitializer>().InitializeAsync(Token);
        _rules = _services.GetRequiredService<FolderRuleService>();
        _accounts = _services.GetRequiredService<AccessService>();

        _tenant = (await _accounts.EnsureDefaultTenantAsync("Library", Token)).Id;
        await _accounts.SetGuestAccessAsync(_tenant, true, Token);
        _users = (await _accounts.GetBuiltInGroupAsync(_tenant, BuiltInGroup.Users, Token))!.Id;
        _guests = (await _accounts.GetBuiltInGroupAsync(_tenant, BuiltInGroup.Guests, Token))!.Id;
        _alice = (await _accounts.CreateUserAsync(new UserInput("alice", Password: "alice-password-1", GroupIds: [_users]), null, Token)).Value!.Id;
    }

    [TestCleanup]
    public async Task CleanupAsync()
    {
        await _services.DisposeAsync();
        SqliteConnection.ClearAllPools();
        _library.Dispose();
    }

    [TestMethod]
    public async Task SetsReplacesAndRemovesARule()
    {
        var set = await _rules.SetAsync(_tenant, "/Private/", FolderPrincipalRef.Group(_users), FolderLevel.Hidden, _alice, Token);
        var replaced = await _rules.SetAsync(_tenant, "Private", FolderPrincipalRef.Group(_users), FolderLevel.Read, null, Token);
        var afterReplace = await _rules.GetAsync(_tenant, Token);
        var removed = await _rules.SetAsync(_tenant, "Private", FolderPrincipalRef.Group(_users), null, null, Token);

        Assert.AreEqual(AccessStatus.Ok, set.Status);
        Assert.AreEqual("Private", set.Value!.Path, "Stored as a root-relative path");
        Assert.AreEqual(_alice, set.Value.CreatedById);
        Assert.AreEqual(set.Value.Id, replaced.Value!.Id, "One rule per principal and path");
        Assert.AreEqual(FolderLevel.Read, afterReplace.Single().Level);
        Assert.IsNull(removed.Value);
        Assert.IsEmpty(await _rules.GetAsync(_tenant, Token));
    }

    [TestMethod]
    public async Task RefusesPathsAndPrincipalsOutsideTheSpace()
    {
        var bob = (await _accounts.CreateUserAsync(new UserInput("bob", Password: "bob-password-12"), null, Token)).Value!.Id;

        Assert.AreEqual(AccessStatus.Invalid, (await _rules.SetAsync(_tenant, "Photos/../..", FolderPrincipalRef.Group(_users), FolderLevel.Hidden, null, Token)).Status);
        Assert.AreEqual(AccessStatus.Invalid, (await _rules.SetAsync(_tenant, "Photos", FolderPrincipalRef.Group(9999), FolderLevel.Hidden, null, Token)).Status);
        Assert.AreEqual(AccessStatus.Invalid, (await _rules.SetAsync(_tenant, "Photos", FolderPrincipalRef.Account(bob), FolderLevel.Read, null, Token)).Status,
            "An account in no group of the space");
        Assert.AreEqual(AccessStatus.Invalid, (await _rules.SetAsync(_tenant, "Photos", new FolderPrincipalRef(_users, _alice), FolderLevel.Read, null, Token)).Status);
        if (OperatingSystem.IsWindows())
            Assert.AreEqual(AccessStatus.Invalid, (await _rules.SetAsync(_tenant, "Private.", FolderPrincipalRef.Group(_users), FolderLevel.Hidden, null, Token)).Status,
                "Windows opens \"Private.\" as \"Private\"");
        Assert.IsEmpty(await _rules.GetAsync(_tenant, Token));
    }

    [TestMethod]
    public async Task ASpaceHasAtMostMaxRules()
    {
        await using (var db = await DbAsync())
        {
            db.FolderRules.AddRange(Enumerable.Range(0, FolderRuleService.MaxRules).Select(i => new FolderRule
            {
                TenantId = _tenant, Path = $"Folder {i}", GroupId = _users, Level = FolderLevel.Read, Created = DateTime.UtcNow,
            }));
            await db.SaveChangesAsync(Token);
        }

        var added = await _rules.SetAsync(_tenant, "One more", FolderPrincipalRef.Group(_users), FolderLevel.Hidden, null, Token);
        var changed = await _rules.SetAsync(_tenant, "Folder 3", FolderPrincipalRef.Group(_users), FolderLevel.Hidden, null, Token);

        Assert.AreEqual(AccessStatus.Conflict, added.Status);
        Assert.AreEqual(AccessStatus.Ok, changed.Status, "Changing a rule is still possible");
    }

    [TestMethod]
    public async Task RulesDecideWhereTheirSubjectsUseTheirRights()
    {
        var evaluator = _services.GetRequiredService<RightsEvaluator>();
        var before = await evaluator.GetAsync(new AccessSubject(_tenant, _alice), Token);

        await _rules.SetAsync(_tenant, "Private", FolderPrincipalRef.Group(_users), FolderLevel.Hidden, null, Token);
        await _rules.SetAsync(_tenant, "Private/Alice", FolderPrincipalRef.Account(_alice), FolderLevel.Write, null, Token);
        await _rules.SetAsync(_tenant, "Public", FolderPrincipalRef.Group(_guests), FolderLevel.Read, null, Token);
        await _rules.SetAsync(_tenant, "", FolderPrincipalRef.Group(_guests), FolderLevel.Hidden, null, Token);
        var alice = await evaluator.GetAsync(new AccessSubject(_tenant, _alice), Token);
        var visitor = await evaluator.GetAsync(new AccessSubject(_tenant, null), Token);

        Assert.IsTrue(before.FolderPolicy.IsUnrestricted);
        Assert.IsFalse(alice.At("Private/Bob/a.jpg").Has(Rights.Browse), "A rule applies at once (the cache follows the changes)");
        Assert.IsTrue(alice.At("Private").PathOnly);
        Assert.IsTrue(alice.At("Private/Alice/a.jpg").Has(Rights.EditImages));
        Assert.IsTrue(alice.At("Elsewhere").Has(Rights.EditImages));
        Assert.IsTrue(alice.Has(Rights.EditImages), "The space's rights themselves don't change");
        Assert.IsFalse(visitor.At("Media").Has(Rights.Browse));
        Assert.IsTrue(visitor.At("Public/a.jpg").Has(Rights.ViewFiles));
        Assert.IsFalse(visitor.At("Public/a.jpg").Has(Rights.EditImages), "Guests never get what changes something");
        Assert.IsTrue(visitor.FilterFor(Rights.Browse).Includes("Public/b.jpg"));
        Assert.IsFalse(visitor.FilterFor(Rights.Browse).Includes("Private/b.jpg"));
    }

    [TestMethod]
    public async Task DeletingAGroupDeletesItsRules()
    {
        var kids = (await _accounts.CreateGroupAsync(_tenant, new GroupInput("Kids", Rights: [Rights.Browse]), cancellationToken: Token)).Value!;
        await _rules.SetAsync(_tenant, "Adult", FolderPrincipalRef.Group(kids.Id), FolderLevel.Hidden, null, Token);
        await _rules.SetAsync(_tenant, "Adult", FolderPrincipalRef.Group(_users), FolderLevel.Read, null, Token);

        var deleted = await _accounts.DeleteGroupAsync(_tenant, kids.Id, Token);

        Assert.AreEqual(AccessStatus.Ok, deleted.Status);
        Assert.AreEqual(_users, (await _rules.GetAsync(_tenant, Token)).Single().Principal.GroupId);
    }

    [TestMethod]
    public async Task MovingAFolderMovesTheRulesOfItsWholeTree()
    {
        await _rules.SetAsync(_tenant, "Photos", FolderPrincipalRef.Group(_users), FolderLevel.Read, null, Token);
        await _rules.SetAsync(_tenant, "Photos/2024/Private", FolderPrincipalRef.Account(_alice), FolderLevel.Write, null, Token);
        await _rules.SetAsync(_tenant, "Photos/2024/Private", FolderPrincipalRef.Group(_guests), FolderLevel.Hidden, null, Token);
        await _rules.SetAsync(_tenant, "Photos2", FolderPrincipalRef.Group(_users), FolderLevel.Hidden, null, Token);
        await _rules.SetAsync(_tenant, "Other", FolderPrincipalRef.Group(_users), FolderLevel.Hidden, null, Token);

        await using var move = (await _rules.BeginMoveAsync(_tenant, "Photos", "Albums/Pictures", false, _alice, Token)).Value!;
        var during = Paths(await _rules.GetAsync(_tenant, Token));
        await move.CompleteAsync(Token);
        var after = await _rules.GetAsync(_tenant, Token);

        CollectionAssert.AreEquivalent(new[] { "Photos", "Photos/2024/Private", "Photos/2024/Private", "Photos2", "Other",
            "Albums/Pictures", "Albums/Pictures/2024/Private", "Albums/Pictures/2024/Private" }, during, "While the disk changes, both places are protected");
        CollectionAssert.AreEquivalent(new[] { "Albums/Pictures", "Albums/Pictures/2024/Private", "Albums/Pictures/2024/Private", "Photos2", "Other" }, Paths(after));
        Assert.AreEqual(FolderLevel.Write, after.Single(r => r.Principal.UserId == _alice).Level);
        Assert.AreEqual(FolderLevel.Hidden, after.Single(r => r.Principal.GroupId == _guests).Level, "Every principal's rules move");
    }

    [TestMethod]
    public async Task AnAbortedMoveLeavesTheRulesAsTheyWere()
    {
        await _rules.SetAsync(_tenant, "Photos", FolderPrincipalRef.Group(_users), FolderLevel.Read, null, Token);
        await _rules.SetAsync(_tenant, "Photos/Deep", FolderPrincipalRef.Group(_guests), FolderLevel.Hidden, null, Token);
        await _rules.SetAsync(_tenant, "Public", FolderPrincipalRef.Group(_guests), FolderLevel.Read, null, Token);
        var before = await _rules.GetAsync(_tenant, Token);

        await using (var move = (await _rules.BeginMoveAsync(_tenant, "Photos", "Pictures", true, null, Token)).Value!)
            await move.AbortAsync(Token);

        CollectionAssert.AreEquivalent(before.ToList(), (await _rules.GetAsync(_tenant, Token)).ToList());
    }

    [TestMethod]
    public async Task AMoveLeftUnfinishedKeepsBothSetsAndFreesTheSpace()
    {
        await _rules.SetAsync(_tenant, "Photos", FolderPrincipalRef.Group(_guests), FolderLevel.Hidden, null, Token);

        var move = (await _rules.BeginMoveAsync(_tenant, "Photos", "Pictures", false, null, Token)).Value!;
        var waiting = _rules.SetAsync(_tenant, "Other", FolderPrincipalRef.Group(_guests), FolderLevel.Hidden, null, Token);
        await Task.Delay(300, Token);
        Assert.IsFalse(waiting.IsCompleted, "Rule changes wait for the move");
        await move.DisposeAsync();
        await waiting.WaitAsync(TimeSpan.FromSeconds(10), Token);

        CollectionAssert.AreEquivalent(new[] { "Photos", "Pictures", "Other" }, Paths(await _rules.GetAsync(_tenant, Token)),
            "A move that never completed (a crash) leaves stale rules, never fewer");
        await Assert.ThrowsExactlyAsync<ObjectDisposedException>(() => move.CompleteAsync(Token));
    }

    [TestMethod]
    public async Task ACaseOnlyRenameRewritesThePaths()
    {
        await _rules.SetAsync(_tenant, "Photos/Private", FolderPrincipalRef.Group(_guests), FolderLevel.Hidden, null, Token);

        await using (var move = (await _rules.BeginMoveAsync(_tenant, "Photos", "photos", false, null, Token)).Value!)
            await move.CompleteAsync(Token);
        var renamed = Paths(await _rules.GetAsync(_tenant, Token));
        await using (var move = (await _rules.BeginMoveAsync(_tenant, "photos", "PHOTOS", false, null, Token)).Value!)
            await move.AbortAsync(Token);

        CollectionAssert.AreEqual(new[] { "photos/Private" }, renamed);
        CollectionAssert.AreEqual(new[] { "photos/Private" }, Paths(await _rules.GetAsync(_tenant, Token)), "Aborted: the old spelling is back");
    }

    [TestMethod]
    public async Task RefusesMovesThatCantHappen()
    {
        await _rules.SetAsync(_tenant, "Pictures/Old", FolderPrincipalRef.Group(_users), FolderLevel.Read, null, Token);

        Assert.AreEqual(AccessStatus.Invalid, (await _rules.BeginMoveAsync(_tenant, "", "Elsewhere", false, null, Token)).Status, "The root");
        Assert.AreEqual(AccessStatus.Invalid, (await _rules.BeginMoveAsync(_tenant, "Photos", "Photos/Inside", false, null, Token)).Status, "Into itself");
        Assert.AreEqual(AccessStatus.Invalid, (await _rules.BeginMoveAsync(_tenant, "Photos", "Photos", false, null, Token)).Status);
        Assert.AreEqual(AccessStatus.Conflict, (await _rules.BeginMoveAsync(_tenant, "Photos", "Pictures", false, null, Token)).Status,
            "Stale rules where the folder goes would apply to it");
        Assert.AreEqual(AccessStatus.Ok, (await _rules.SetAsync(_tenant, "Free", FolderPrincipalRef.Group(_users), FolderLevel.Read, null, Token)).Status,
            "A refused move holds no lock");
    }

    [TestMethod]
    public async Task AMoveTellsWhoGainsAccessAndCanKeepTheOldAccess()
    {
        await _rules.SetAsync(_tenant, "Private", FolderPrincipalRef.Group(_guests), FolderLevel.Hidden, null, Token);
        await _rules.SetAsync(_tenant, "Private", FolderPrincipalRef.Account(_alice), FolderLevel.Write, null, Token);
        await _rules.SetAsync(_tenant, "Public", FolderPrincipalRef.Group(_users), FolderLevel.Read, null, Token);
        var evaluator = _services.GetRequiredService<RightsEvaluator>();

        var changes = (await _rules.CompareMoveAsync(_tenant, "Private/Holiday", "Public/Holiday", Token)).Value!;
        await using (var move = (await _rules.BeginMoveAsync(_tenant, "Private/Holiday", "Public/Holiday", true, null, Token)).Value!)
            await move.CompleteAsync(Token);
        var visitor = await evaluator.GetAsync(new AccessSubject(_tenant, null), Token);
        var alice = await evaluator.GetAsync(new AccessSubject(_tenant, _alice), Token);

        var guests = changes.Single(c => c.Principal.GroupId == _guests);
        Assert.AreEqual(FolderLevel.Hidden, guests.Before);
        Assert.AreEqual(FolderLevel.Write, guests.After);
        Assert.IsTrue(guests.Gains);
        var users = changes.Single(c => c.Principal.GroupId == _users);
        Assert.IsFalse(users.Gains, "Users lose Write: Public is read-only for them");
        var account = changes.Single(c => c.Principal.UserId == _alice);
        Assert.IsNull(account.After, "Alice's own rule stays on Private");
        Assert.IsFalse(account.Gains);
        Assert.IsFalse(visitor.At("Public/Holiday/a.jpg").Has(Rights.Browse), "Kept: guests still don't see it");
        Assert.IsTrue(alice.At("Public/Holiday/a.jpg").Has(Rights.EditImages), "Kept: Alice may still change it");
        Assert.IsTrue(visitor.At("Public/Other.jpg").Has(Rights.Browse));
    }

    private async Task<AccessDbContext> DbAsync() =>
        await _services.GetRequiredService<IDbContextFactory<AccessDbContext>>().CreateDbContextAsync(Token);

    private static List<string> Paths(IEnumerable<FolderRuleInfo> rules) => [.. rules.Select(r => r.Path)];
}

/// <summary>The index filter of folder rules, run on SQLite, picks the files the policy allows.</summary>
[TestClass]
public sealed class FolderFilterIndexTests
{
    private TestLibrary _library = null!;
    private CoreServices _services = null!;

    public TestContext TestContext { get; set; } = null!;

    [TestInitialize]
    public async Task InitializeAsync()
    {
        _library = new TestLibrary();
        _services = await CoreServices.CreateAsync(_library);
        await _services.Get<Sfe.Core.Indexing.DirectoryIndexer>().IndexAsync(_library.Root, true, new Sfe.Core.Indexing.ScanProgress(), TestContext.CancellationToken);
    }

    [TestCleanup]
    public async Task CleanupAsync()
    {
        await _services.DisposeAsync();
        SqliteConnection.ClearAllPools();
        _library.Dispose();
    }

    [TestMethod]
    public async Task AFolderRuleCoversItsTreeOnly()
    {
        var policy = FolderPolicy.Compile([new FolderPrincipal(new Dictionary<string, FolderLevel>
        {
            ["ChildDir"] = FolderLevel.Hidden,
            ["RepeatedDir"] = FolderLevel.Hidden,
            ["RepeatedDir/RepeatedDir"] = FolderLevel.Read,
        }, new HashSet<string> { Rights.Browse }, true)]);

        var files = await FilesAsync(policy.FilterFor(Rights.Browse, new HashSet<string> { Rights.Browse }));

        CollectionAssert.Contains(files, "100%_done[1].txt", "LIKE wildcards in names are matched literally");
        CollectionAssert.Contains(files, "RepeatedDir/RepeatedDir/RDFile1.txt");
        CollectionAssert.DoesNotContain(files, "ChildDir/AFile1.txt");
        CollectionAssert.DoesNotContain(files, "RepeatedDir/ChildDir/AFile1.txt");
        CollectionAssert.DoesNotContain(files, "RepeatedDir/RDFile1.txt");
        CollectionAssert.Contains(files, "Media/photo.jpg");
    }

    [TestMethod]
    [DataRow(11)]
    [DataRow(12)]
    [DataRow(13)]
    [DataRow(14)]
    [DataRow(15)]
    [DataRow(16)]
    public async Task TheIndexFilterPicksWhatThePolicyAllows(int seed)
    {
        var paths = _services.Get<LibraryPaths>();
        await using var db = await _services.Get<IDbContextFactory<SfeDbContext>>().CreateDbContextAsync(TestContext.CancellationToken);
        var folders = (await db.Directories.Select(d => d.FullName).ToListAsync(TestContext.CancellationToken)).Select(f => paths.ToRelative(f)!).ToList();
        var all = (await db.Files.Select(f => f.FullName).ToListAsync(TestContext.CancellationToken)).Select(f => paths.ToRelative(f)!).ToList();
        var random = new Random(seed);
        var principals = Enumerable.Range(0, 2)
            .Select(_ => new FolderPrincipal(FolderPolicyTests.RandomRules(random, folders).ToDictionary(r => r.Item1, r => r.Item2), new HashSet<string> { Rights.Browse }, true))
            .ToList();
        var granted = new HashSet<string> { Rights.Browse };
        var policy = FolderPolicy.Compile(principals);
        var filter = policy.FilterFor(Rights.Browse, granted);

        var files = await FilesAsync(filter);
        var shownFolders = (await db.Directories.Where(filter.ToPredicate<DirectoryEntry>(d => d.FullName, paths)).Select(d => d.FullName).ToListAsync(TestContext.CancellationToken))
            .Select(f => paths.ToRelative(f)!).ToList();

        CollectionAssert.AreEquivalent(all.Where(f => policy.At(f, granted).Has(Rights.Browse)).ToList(), files, $"seed {seed}");
        CollectionAssert.AreEquivalent(folders.Where(f => policy.At(f, granted).Has(Rights.Browse)).ToList(), shownFolders, $"seed {seed}");
    }

    private async Task<List<string>> FilesAsync(PathFilter filter)
    {
        var paths = _services.Get<LibraryPaths>();
        await using var db = await _services.Get<IDbContextFactory<SfeDbContext>>().CreateDbContextAsync(TestContext.CancellationToken);
        var names = await db.Files.Where(filter.ToPredicate<FileEntry>(f => f.FullName, paths)).Select(f => f.FullName).ToListAsync(TestContext.CancellationToken);
        return [.. names.Select(n => paths.ToRelative(n)!)];
    }
}

/// <summary>The update of an install made before folder rules.</summary>
[TestClass]
public sealed class FolderRulesUpgradeTests
{
    private TestLibrary _library = null!;

    public TestContext TestContext { get; set; } = null!;

    [TestInitialize]
    public void Initialize() => _library = new TestLibrary();

    [TestCleanup]
    public void Cleanup()
    {
        SqliteConnection.ClearAllPools();
        _library.Dispose();
    }

    /// <summary>
    /// An install of 1.51.0 has no FolderRules table: the update adds it to the SQLite file, and nothing changes for
    /// anyone (no rules: every right applies everywhere).
    /// </summary>
    [TestMethod]
    public async Task AnUpdatedInstallGetsTheTable()
    {
        await using (var factory = ApiClients.CreateFactory(_library))
            await ApiClients.SetUpAsync(factory, TestContext.CancellationToken);
        await using (var connection = new SqliteConnection(_library.ConnectionString))
        {
            await connection.OpenAsync(TestContext.CancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = $"""
                DROP TABLE "FolderRules";
                UPDATE "{AccessDbContext.ServerPropertiesTable}" SET "Value" = '1.51.0' WHERE "Key" = '{ServerProperty.VersionKey}';
                PRAGMA wal_checkpoint(TRUNCATE);
                """;
            await command.ExecuteNonQueryAsync(TestContext.CancellationToken);
        }

        await using var updated = ApiClients.CreateFactory(_library);
        await ApiClients.MigrateAsync(updated, TestContext.CancellationToken);
        var accounts = updated.Services.GetRequiredService<AccessService>();
        var tenant = (await accounts.GetTenantsAsync(TestContext.CancellationToken)).Single(t => t.IsDefault).Id;
        var admin = (await accounts.GetServerAdminIdsAsync(TestContext.CancellationToken)).Single();
        var users = (await accounts.GetBuiltInGroupAsync(tenant, BuiltInGroup.Users, TestContext.CancellationToken))!.Id;
        var rules = updated.Services.GetRequiredService<FolderRuleService>();

        Assert.IsEmpty(await rules.GetAsync(tenant, TestContext.CancellationToken));
        Assert.AreEqual(AccessStatus.Ok, (await rules.SetAsync(tenant, "Media", FolderPrincipalRef.Group(users), FolderLevel.Read, admin, TestContext.CancellationToken)).Status);
    }
}
