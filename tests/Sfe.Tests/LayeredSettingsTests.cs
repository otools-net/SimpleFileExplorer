using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.Testing;
using Sfe.Api.Endpoints;
using Sfe.Core.Access;
using Sfe.Core.Data;
using Sfe.Core.Indexing;
using Sfe.Core.Library;
using Sfe.Core.Options;
using Sfe.Core.Search;
using Sfe.Core.Settings;
using Sfe.Tests.Infrastructure;

namespace Sfe.Tests;

/// <summary>
/// Settings by level: the configuration, the server's settings page, a library, a user; locks at each level; the
/// rights of a library's administrators; the upgrade of a server set up before levels existed.
/// </summary>
[TestClass]
public sealed class LayeredSettingsTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private TestLibrary _library = null!;
    private readonly List<IDisposable> _disposables = [];

    public TestContext TestContext { get; set; } = null!;

    [TestInitialize]
    public void Initialize() => _library = new TestLibrary();

    [TestCleanup]
    public void Cleanup()
    {
        foreach (var disposable in Enumerable.Reverse(_disposables))
            disposable.Dispose();
        _library.Dispose();
    }

    [TestMethod]
    public async Task ALibraryOverridesTheServersDefaults()
    {
        var admin = await AdminAsync(Factory());

        await SaveAsync(admin, "server", s => s.Player.SeekSeconds = 15);
        var serverDefault = (await ConfigAsync(admin)).SeekSeconds;
        await SaveAsync(admin, "tenant", s => s.Player.SeekSeconds = 20);
        var library = await SettingsAsync(admin, "tenant");
        var libraryValue = (await ConfigAsync(admin)).SeekSeconds;
        using (var reset = await admin.DeleteAsync("/api/settings?level=tenant", TestContext.CancellationToken))
            Assert.AreEqual(HttpStatusCode.OK, reset.StatusCode);

        Assert.AreEqual(15, serverDefault);
        Assert.AreEqual(20, libraryValue);
        Assert.AreEqual(SettingLevel.Tenant, library.Level);
        Assert.IsTrue(library.Customized);
        Assert.AreEqual(SettingLevel.Tenant, library.Keys!["player.seekSeconds"].Source);
        Assert.AreEqual(15, library.Keys["player.seekSeconds"].Inherited!.GetValue<int>());
        Assert.AreEqual(15, (await ConfigAsync(admin)).SeekSeconds);
    }

    [TestMethod]
    public async Task OnlyChangedValuesAreKeptSoTheConfigurationStillApplies()
    {
        var admin = await AdminAsync(Factory());

        await SaveAsync(admin, "server", s => s.Player.SeekSeconds = 15);
        var file = JsonNode.Parse(await File.ReadAllTextAsync(_library.SettingsPath, TestContext.CancellationToken))!.AsObject();

        Assert.AreEqual(15, file["player"]?["seekSeconds"]?.GetValue<int>());
        Assert.IsNull(file["browse"], "Settings left as configured are not written");
    }

    [TestMethod]
    public async Task TheConfigurationCanLockASettingForEveryLevel()
    {
        var admin = await AdminAsync(Factory(settings =>
        {
            settings["Sfe:Locked:0"] = "player.*";
            settings["Sfe:Player:SeekSeconds"] = "7";
        }));

        var view = await SettingsAsync(admin, "server");
        using var server = await PutAsync(admin, "server", view.Values, s => s.Player.SeekSeconds = 15);
        using var library = await PutAsync(admin, "tenant", view.Values, s => s.Player.Loop = true);

        Assert.IsFalse(view.Keys!["player.seekSeconds"].Editable);
        Assert.AreEqual(SettingLevel.Environment, view.Keys["player.seekSeconds"].LockedBy);
        Assert.IsTrue(view.Keys["browse.showDuration"].Editable);
        Assert.AreEqual(HttpStatusCode.BadRequest, server.StatusCode);
        StringAssert.Contains(await server.Content.ReadAsStringAsync(TestContext.CancellationToken), "player.seekSeconds");
        Assert.AreEqual(HttpStatusCode.BadRequest, library.StatusCode);
        Assert.AreEqual(7, (await ConfigAsync(admin)).SeekSeconds);
    }

    [TestMethod]
    public async Task TheServerCanLockASettingForLibrariesAndUsers()
    {
        var admin = await AdminAsync(Factory());

        await SaveAsync(admin, "server", s => s.Player.SeekSeconds = 15);
        using (var locked = await admin.PutAsJsonAsync("/api/settings/locks?level=server", new SettingLocks(["player.seekSeconds"]), TestContext.CancellationToken))
            Assert.AreEqual(HttpStatusCode.OK, locked.StatusCode);
        var library = await SettingsAsync(admin, "tenant");
        using var refused = await PutAsync(admin, "tenant", library.Values, s => s.Player.SeekSeconds = 20);
        using var serverOnly = await admin.PutAsJsonAsync("/api/settings/locks?level=server", new SettingLocks(["scan.parallelism"]), TestContext.CancellationToken);

        Assert.AreEqual(SettingLevel.Server, library.Keys!["player.seekSeconds"].LockedBy);
        Assert.AreEqual(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, serverOnly.StatusCode, "Nothing below the server sets it: nothing to lock");
        Assert.AreEqual(15, (await ConfigAsync(admin)).SeekSeconds);
    }

    [TestMethod]
    public async Task ServerSettingsCannotChangeInALibrary()
    {
        var admin = await AdminAsync(Factory());

        var library = await SettingsAsync(admin, "tenant");
        using var refused = await PutAsync(admin, "tenant", library.Values, s => s.Scan.Parallelism = 9);
        using var perLibrary = await PutAsync(admin, "server", (await SettingsAsync(admin, "server")).Values, s => s.Trickplay.Folders = ["Media"]);

        Assert.AreEqual(SettingLevel.Server, library.Keys!["scan.parallelism"].Scope);
        Assert.IsFalse(library.Keys["scan.parallelism"].Editable);
        Assert.AreEqual(HttpStatusCode.BadRequest, refused.StatusCode);
        StringAssert.Contains(await refused.Content.ReadAsStringAsync(TestContext.CancellationToken), "scan.parallelism");
        Assert.AreEqual(HttpStatusCode.BadRequest, perLibrary.StatusCode, "Folders of a library are set in the library");
    }

    [TestMethod]
    public async Task UsersPreferencesWinUnlessTheLibraryLocksThem()
    {
        var factory = Factory();
        var admin = await AdminAsync(factory);
        var users = await GroupAsync(admin, BuiltInGroup.Users);
        await CreateUserAsync(admin, new UserInput("alice", Password: "alice-password", GroupIds: [users.Id]));
        var alice = await ApiClients.SignInAsync(factory, "alice", "alice-password", TestContext.CancellationToken);

        var preferences = await GetAsync<SettingsView>(alice, "/api/me/preferences");
        using (var saved = await PutPreferencesAsync(alice, preferences.Values, s => s.Player.SeekSeconds = 30))
            Assert.AreEqual(HttpStatusCode.OK, saved.StatusCode);
        using var notMine = await PutPreferencesAsync(alice, preferences.Values, s => s.Scan.ComputeHashes = false);
        var alicesOwn = (await ConfigAsync(alice)).SeekSeconds;
        var adminsOwn = (await ConfigAsync(admin)).SeekSeconds;
        using (var locked = await admin.PutAsJsonAsync("/api/settings/locks?level=tenant", new SettingLocks(["player.seekSeconds"]), TestContext.CancellationToken))
            Assert.AreEqual(HttpStatusCode.OK, locked.StatusCode);
        var afterLock = (await ConfigAsync(alice)).SeekSeconds;
        using var refused = await PutPreferencesAsync(alice, (await GetAsync<SettingsView>(alice, "/api/me/preferences")).Values, s => s.Player.SeekSeconds = 40);

        Assert.IsFalse(preferences.Keys!["scan.computeHashes"].Editable);
        Assert.IsTrue(preferences.Keys["player.seekSeconds"].Editable);
        Assert.AreEqual(HttpStatusCode.BadRequest, notMine.StatusCode);
        Assert.AreEqual(30, alicesOwn);
        Assert.AreEqual(10, adminsOwn, "Preferences are each user's own");
        Assert.AreEqual(10, afterLock);
        Assert.AreEqual(HttpStatusCode.BadRequest, refused.StatusCode);
    }

    [TestMethod]
    public async Task GuestsKeepTheirPreferencesInTheBrowser()
    {
        var factory = Factory();
        var admin = await AdminAsync(factory);
        using (await admin.PutAsJsonAsync("/api/access/tenant/guest-access", new GuestAccessChange(true), TestContext.CancellationToken)) { }

        using var preferences = await ApiClients.Create(factory).GetAsync("/api/me/preferences", TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.Unauthorized, preferences.StatusCode);
    }

    [TestMethod]
    public async Task LibraryAdministratorsChangeTheSectionsTheyHaveRightsFor()
    {
        var factory = Factory();
        var admin = await AdminAsync(factory);
        using var created = await admin.PostAsJsonAsync("/api/access/groups",
            new GroupInput("Playback", Rights: [Rights.Browse, Rights.PlaybackSettings]), Json, TestContext.CancellationToken);
        var group = (await created.Content.ReadFromJsonAsync<GroupInfo>(Json, TestContext.CancellationToken))!;
        await CreateUserAsync(admin, new UserInput("carol", Password: "carol-password", GroupIds: [group.Id]));
        var carol = await ApiClients.SignInAsync(factory, "carol", "carol-password", TestContext.CancellationToken);

        var view = await SettingsAsync(carol, null);
        using var playback = await PutAsync(carol, null, view.Values, s => s.Player.SeekSeconds = 25);
        using var scanning = await PutAsync(carol, null, view.Values, s => s.Scan.ComputeHashes = false);
        using var server = await carol.GetAsync("/api/settings?level=server", TestContext.CancellationToken);

        Assert.AreEqual(SettingLevel.Tenant, view.Level, "The library's level is the default without the server's right");
        Assert.IsTrue(view.Keys!["player.seekSeconds"].Editable);
        Assert.IsFalse(view.Keys["scan.computeHashes"].Editable);
        Assert.AreEqual(HttpStatusCode.OK, playback.StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, scanning.StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden, server.StatusCode);
    }

    [TestMethod]
    public async Task ChangesSaveOnlyTheSettingsSentSoTheOthersFollowTheServer()
    {
        var admin = await AdminAsync(Factory());

        await PatchOkAsync(admin, "server", new() { ["player.seekSeconds"] = 15 });
        await PatchOkAsync(admin, "tenant", new() { ["player.loop"] = true });
        // A server change after the library's: the library never wrote the seek step, so it follows.
        await PatchOkAsync(admin, "server", new() { ["player.seekSeconds"] = 20 });
        var library = await SettingsAsync(admin, "tenant");
        var file = JsonNode.Parse(await File.ReadAllTextAsync(_library.SettingsPath, TestContext.CancellationToken))!.AsObject();

        Assert.AreEqual(20, (await ConfigAsync(admin)).SeekSeconds);
        Assert.AreEqual(SettingLevel.Server, library.Keys!["player.seekSeconds"].Source);
        Assert.AreEqual(SettingLevel.Tenant, library.Keys["player.loop"].Source);
        Assert.IsTrue(library.Values.Player.Loop);
        Assert.AreEqual(20, file["player"]?["seekSeconds"]?.GetValue<int>());
        Assert.IsNull(file["player"]?["loop"], "The library's change stays in the library");
        Assert.IsNull(file["browse"], "Settings not sent are not written");
    }

    [TestMethod]
    public async Task ChangesKeepWhatTheLevelAlreadyHas()
    {
        var admin = await AdminAsync(Factory());

        await PatchOkAsync(admin, "tenant", new() { ["player.seekSeconds"] = 25 });
        await PatchOkAsync(admin, "tenant", new() { ["player.loop"] = true });
        var library = await SettingsAsync(admin, "tenant");

        Assert.AreEqual(25, library.Values.Player.SeekSeconds);
        Assert.IsTrue(library.Values.Player.Loop);
        Assert.AreEqual(SettingLevel.Tenant, library.Keys!["player.seekSeconds"].Source);
    }

    [TestMethod]
    public async Task AChangeBackToTheInheritedValueFollowsTheLevelsAboveAgain()
    {
        var admin = await AdminAsync(Factory());

        await PatchOkAsync(admin, "tenant", new() { ["player.seekSeconds"] = 25 });
        var inherited = (await SettingsAsync(admin, "tenant")).Keys!["player.seekSeconds"].Inherited!.GetValue<int>();
        await PatchOkAsync(admin, "tenant", new() { ["player.seekSeconds"] = inherited });
        await PatchOkAsync(admin, "server", new() { ["player.seekSeconds"] = 12 });
        var library = await SettingsAsync(admin, "tenant");

        Assert.AreEqual(SettingLevel.Server, library.Keys!["player.seekSeconds"].Source);
        Assert.IsFalse(library.Customized, "No row is left for the library");
        Assert.AreEqual(12, library.Values.Player.SeekSeconds);
    }

    [TestMethod]
    public async Task ChangesAreNormalizedLikeAWholeSave()
    {
        var admin = await AdminAsync(Factory());

        await PatchOkAsync(admin, "tenant", new() { ["fileTypes.images"] = new JsonArray(".JPG", "png", "jpg"), ["player.externalName"] = "  " });
        var library = await SettingsAsync(admin, "tenant");

        CollectionAssert.AreEqual(new[] { "jpg", "png" }, library.Values.FileTypes.Images.ToArray());
        Assert.IsNull(library.Values.Player.ExternalName);
    }

    [TestMethod]
    public async Task RefusedChangesSaveNothing()
    {
        var admin = await AdminAsync(Factory());

        using var outOfRange = await PatchAsync(admin, "tenant", new() { ["player.seekSeconds"] = 0, ["player.loop"] = true });
        using var unknown = await PatchAsync(admin, "tenant", new() { ["player.nothing"] = 1, ["player.loop"] = true });
        using var wrongType = await PatchAsync(admin, "tenant", new() { ["player.seekSeconds"] = "soon", ["player.loop"] = true });
        using var serverOnly = await PatchAsync(admin, "tenant", new() { ["scan.parallelism"] = 9, ["player.loop"] = true });
        // Checked with the values in effect: the page size may not exceed the largest page the server allows.
        using var tooLarge = await PatchAsync(admin, "tenant", new() { ["browse.defaultPageSize"] = 100_000 });
        var library = await SettingsAsync(admin, "tenant");

        foreach (var (response, key) in new[] { (outOfRange, "player.seekSeconds"), (unknown, "player.nothing"), (wrongType, "player.seekSeconds"),
            (serverOnly, "scan.parallelism"), (tooLarge, "browse.defaultPageSize") })
        {
            Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode, key);
            StringAssert.Contains(await response.Content.ReadAsStringAsync(TestContext.CancellationToken), key);
        }
        Assert.IsFalse(library.Values.Player.Loop, "Nothing of a refused change is saved");
        Assert.IsFalse(library.Customized);
    }

    [TestMethod]
    public async Task AnUnchangedValueOfASettingTheLevelCannotChangeIsAccepted()
    {
        var admin = await AdminAsync(Factory());

        var parallelism = (await SettingsAsync(admin, "tenant")).Values.Scan.Parallelism;
        await PatchOkAsync(admin, "tenant", new() { ["scan.parallelism"] = parallelism, ["player.loop"] = true });

        Assert.IsTrue((await SettingsAsync(admin, "tenant")).Values.Player.Loop);
    }

    [TestMethod]
    public async Task ChangesLockAndUnlockWithTheValues()
    {
        var factory = Factory();
        var admin = await AdminAsync(factory);
        var users = await GroupAsync(admin, BuiltInGroup.Users);
        await CreateUserAsync(admin, new UserInput("alice", Password: "alice-password", GroupIds: [users.Id]));
        var alice = await ApiClients.SignInAsync(factory, "alice", "alice-password", TestContext.CancellationToken);

        await PatchOkAsync(admin, "tenant", new() { ["player.seekSeconds"] = 25 }, lockKeys: ["player.seekSeconds", "player.loop"]);
        var locked = await SettingsAsync(admin, "tenant");
        using var refused = await PatchPreferencesAsync(alice, new() { ["player.seekSeconds"] = 40 });
        using var notLockable = await PatchAsync(admin, "tenant", new(), lockKeys: ["scan.parallelism"]);
        using var both = await PatchAsync(admin, "tenant", new(), lockKeys: ["player.loop"], unlockKeys: ["player.loop"]);
        await PatchOkAsync(admin, "tenant", new(), unlockKeys: ["player.seekSeconds", "player.loop"]);
        var unlocked = await SettingsAsync(admin, "tenant");
        using var allowed = await PatchPreferencesAsync(alice, new() { ["player.seekSeconds"] = 40 });

        Assert.IsTrue(locked.Keys!["player.seekSeconds"].Locked);
        Assert.IsTrue(locked.Keys["player.loop"].Locked, "A lock without a value pins the value in effect");
        Assert.AreEqual(25, locked.Values.Player.SeekSeconds);
        Assert.AreEqual(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, notLockable.StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, both.StatusCode);
        Assert.IsFalse(unlocked.Keys!["player.seekSeconds"].Locked);
        Assert.AreEqual(25, unlocked.Values.Player.SeekSeconds, "Unlocking keeps the library's own value");
        Assert.AreNotEqual(SettingLevel.Tenant, unlocked.Keys["player.loop"].Source, "...and drops the row a lock alone made");
        Assert.AreEqual(HttpStatusCode.OK, allowed.StatusCode);
        Assert.AreEqual(40, (await ConfigAsync(alice)).SeekSeconds);
    }

    [TestMethod]
    public async Task TheServerLocksThroughChangesToo()
    {
        var admin = await AdminAsync(Factory());

        await PatchOkAsync(admin, "server", new() { ["player.seekSeconds"] = 15 }, lockKeys: ["player.seekSeconds"]);
        await PatchOkAsync(admin, "server", new() { ["player.loop"] = true });
        var server = await SettingsAsync(admin, "server");
        using var refused = await PatchAsync(admin, "tenant", new() { ["player.seekSeconds"] = 20 });

        Assert.IsTrue(server.Keys!["player.seekSeconds"].Locked, "A change without lock changes keeps the locks");
        Assert.AreEqual(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.AreEqual(15, (await ConfigAsync(admin)).SeekSeconds);
    }

    [TestMethod]
    public async Task PreferenceChangesKeepFollowingTheLibraryForTheRest()
    {
        var factory = Factory();
        var admin = await AdminAsync(factory);
        var users = await GroupAsync(admin, BuiltInGroup.Users);
        await CreateUserAsync(admin, new UserInput("alice", Password: "alice-password", GroupIds: [users.Id]));
        var alice = await ApiClients.SignInAsync(factory, "alice", "alice-password", TestContext.CancellationToken);

        using (var saved = await PatchPreferencesAsync(alice, new() { ["player.seekSeconds"] = 30 }))
            Assert.AreEqual(HttpStatusCode.OK, saved.StatusCode, await saved.Content.ReadAsStringAsync(TestContext.CancellationToken));
        using var notMine = await PatchPreferencesAsync(alice, new() { ["scan.computeHashes"] = false });
        using var locks = await PatchPreferencesAsync(alice, new(), lockKeys: ["player.loop"]);
        await PatchOkAsync(admin, "tenant", new() { ["browse.showDuration"] = false });
        var preferences = await GetAsync<SettingsView>(alice, "/api/me/preferences");

        Assert.AreEqual(30, (await ConfigAsync(alice)).SeekSeconds);
        Assert.AreEqual(HttpStatusCode.BadRequest, notMine.StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, locks.StatusCode, "Users lock nothing");
        Assert.IsFalse(preferences.Values.Browse.ShowDuration, "The library's later change still reaches the user");
        Assert.AreEqual(SettingLevel.Tenant, preferences.Keys!["browse.showDuration"].Source);
    }

    [TestMethod]
    public async Task LibraryAdministratorsChangeOnlyTheirSectionsThroughChanges()
    {
        var factory = Factory();
        var admin = await AdminAsync(factory);
        using var created = await admin.PostAsJsonAsync("/api/access/groups",
            new GroupInput("Playback", Rights: [Rights.Browse, Rights.PlaybackSettings]), Json, TestContext.CancellationToken);
        var group = (await created.Content.ReadFromJsonAsync<GroupInfo>(Json, TestContext.CancellationToken))!;
        await CreateUserAsync(admin, new UserInput("carol", Password: "carol-password", GroupIds: [group.Id]));
        var carol = await ApiClients.SignInAsync(factory, "carol", "carol-password", TestContext.CancellationToken);
        await PatchOkAsync(admin, "tenant", new() { ["scan.computeHashes"] = false });

        using var playback = await PatchAsync(carol, null, new() { ["player.seekSeconds"] = 25 });
        using var scanning = await PatchAsync(carol, null, new() { ["scan.computeHashes"] = true });
        using var server = await PatchAsync(carol, "server", new() { ["player.seekSeconds"] = 25 });
        var library = await SettingsAsync(admin, "tenant");

        Assert.AreEqual(HttpStatusCode.OK, playback.StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, scanning.StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden, server.StatusCode);
        Assert.IsFalse(library.Values.Scan.ComputeHashes, "A section without the right keeps the library's value");
        Assert.AreEqual(25, library.Values.Player.SeekSeconds);
    }

    [TestMethod]
    public void EverySettingHasALevelAndARight()
    {
        var keys = SettingKeys.Flatten(SettingKeys.ToJson(new EditableSettings())).Keys.ToList();

        CollectionAssert.AreEquivalent(keys, SettingCatalog.All.Select(d => d.Key).ToList());
        // A library section's right; server.settings itself only for what concerns the server alone (how it starts).
        Assert.IsTrue(SettingCatalog.All.All(d => RightCatalog.Find(d.Right) is { Scope: RightScope.Tenant }
                                                  || d.Right == Rights.ServerSettings && d.Scope == SettingLevel.Server));
        Assert.AreEqual(SettingLevel.Server, SettingCatalog.Find("openBrowser")!.Scope);
        Assert.AreEqual(Rights.ServerSettings, SettingCatalog.Find("fastSignIn")!.Right);
        Assert.AreEqual(SettingLevel.User, SettingCatalog.Find("appearance.theme")!.Scope);
        Assert.AreEqual(SettingLevel.Server, SettingCatalog.Find("scan.parallelism")!.Scope);
        Assert.IsTrue(SettingCatalog.Find("scan.excludedFolders")!.PerLibrary);
    }

    [TestMethod]
    public async Task AnUpgradedServerKeepsItsIndexAndSettings()
    {
        // The index and the settings file of the version before accounts: a scanned library, every setting saved.
        await using (var core = await CoreServices.CreateAsync(_library))
            await core.Get<DirectoryIndexer>().IndexAsync(_library.Root, true, new ScanProgress(), TestContext.CancellationToken);
        var defaults = new SfeOptions();
        defaults.FileTypes.ApplyDefaults(_ => false);
        var saved = EditableSettings.From(defaults);
        saved.Player.SeekSeconds = 25;
        saved.Scan.ExcludedFolders = ["Media"];
        var old = SettingKeys.ToJson(saved);
        old["player"]!.AsObject().Remove("linkLifetimeMinutes");
        await File.WriteAllTextAsync(_library.SettingsPath, old.ToJsonString(), TestContext.CancellationToken);

        var factory = Factory();
        await ApiClients.MigrateAsync(factory, TestContext.CancellationToken);
        var admin = await AdminAsync(factory);
        using var search = await admin.GetAsync("/api/search?q=photo", TestContext.CancellationToken);
        var results = (await search.Content.ReadFromJsonAsync<SearchResults>(Json, TestContext.CancellationToken))!;
        var library = await SettingsAsync(admin, "tenant");
        var server = await SettingsAsync(admin, "server");
        var file = JsonNode.Parse(await File.ReadAllTextAsync(_library.SettingsPath, TestContext.CancellationToken))!.AsObject();

        Assert.AreEqual(HttpStatusCode.OK, search.StatusCode);
        Assert.AreEqual("Media/photo.jpg", results.Items.Single().Path, "The index is the first library's, unchanged");
        Assert.AreEqual(25, (await ConfigAsync(admin)).SeekSeconds, "The saved settings still apply");
        CollectionAssert.Contains(library.Values.Scan.ExcludedFolders, "Media", "Folders of the library moved to the library");
        Assert.IsEmpty(server.Values.Scan.ExcludedFolders);
        Assert.IsNull(file["scan"]?["excludedFolders"], "...and left the server's settings file");
    }

    private WebApplicationFactory<Program> Factory(Action<IDictionary<string, string?>>? configure = null)
    {
        var factory = ApiClients.CreateFactory(_library, configure);
        _disposables.Add(factory);
        return factory;
    }

    private async Task<HttpClient> AdminAsync(WebApplicationFactory<Program> factory)
    {
        var client = await ApiClients.SetUpAsync(factory, TestContext.CancellationToken);
        _disposables.Add(client);
        return client;
    }

    private async Task SaveAsync(HttpClient client, string level, Action<EditableSettings> change)
    {
        using var response = await PutAsync(client, level, (await SettingsAsync(client, level)).Values, change);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync(TestContext.CancellationToken));
    }

    private Task<HttpResponseMessage> PutAsync(HttpClient client, string? level, EditableSettings values, Action<EditableSettings> change)
    {
        var copy = SettingKeys.FromJson(SettingKeys.ToJson(values));
        change(copy);
        return client.PutAsJsonAsync(level is null ? "/api/settings" : $"/api/settings?level={level}", copy, Json, TestContext.CancellationToken);
    }

    private Task<HttpResponseMessage> PutPreferencesAsync(HttpClient client, EditableSettings values, Action<EditableSettings> change)
    {
        var copy = SettingKeys.FromJson(SettingKeys.ToJson(values));
        change(copy);
        return client.PutAsJsonAsync("/api/me/preferences", copy, Json, TestContext.CancellationToken);
    }

    private Task<HttpResponseMessage> PatchAsync(HttpClient client, string? level, Dictionary<string, JsonNode?> values,
        string[]? lockKeys = null, string[]? unlockKeys = null) =>
        client.PatchAsJsonAsync(level is null ? "/api/settings" : $"/api/settings?level={level}",
            new SettingChanges(values, lockKeys, unlockKeys), Json, TestContext.CancellationToken);

    private async Task PatchOkAsync(HttpClient client, string level, Dictionary<string, JsonNode?> values,
        string[]? lockKeys = null, string[]? unlockKeys = null)
    {
        using var response = await PatchAsync(client, level, values, lockKeys, unlockKeys);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync(TestContext.CancellationToken));
    }

    private Task<HttpResponseMessage> PatchPreferencesAsync(HttpClient client, Dictionary<string, JsonNode?> values, string[]? lockKeys = null) =>
        client.PatchAsJsonAsync("/api/me/preferences", new SettingChanges(values, lockKeys), Json, TestContext.CancellationToken);

    private Task<SettingsView> SettingsAsync(HttpClient client, string? level) =>
        GetAsync<SettingsView>(client, level is null ? "/api/settings" : $"/api/settings?level={level}");

    private Task<ClientConfig> ConfigAsync(HttpClient client) => GetAsync<ClientConfig>(client, "/api/config");

    private async Task<GroupInfo> GroupAsync(HttpClient admin, BuiltInGroup builtIn) =>
        (await GetAsync<List<GroupInfo>>(admin, "/api/access/groups")).Single(g => g.BuiltIn == builtIn);

    private async Task CreateUserAsync(HttpClient admin, UserInput input)
    {
        using var response = await admin.PostAsJsonAsync("/api/access/users", input, Json, TestContext.CancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode, await response.Content.ReadAsStringAsync(TestContext.CancellationToken));
    }

    private async Task<T> GetAsync<T>(HttpClient client, string url)
    {
        using var response = await client.GetAsync(url, TestContext.CancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, $"{url}: {await response.Content.ReadAsStringAsync(TestContext.CancellationToken)}");
        return (await response.Content.ReadFromJsonAsync<T>(Json, TestContext.CancellationToken))!;
    }
}
